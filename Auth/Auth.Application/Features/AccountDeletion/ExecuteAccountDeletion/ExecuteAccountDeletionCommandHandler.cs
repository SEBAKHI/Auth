using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;

/// <summary>
/// Handler for staged account destruction: optimistic claim, unified purge
/// (tombstone → crypto-shred → anonymize → cascade), best-effort profile
/// image cleanup, completion bookkeeping and the completed event. Failures
/// return the request to the grace queue for retry and dead-letter it as
/// Failed once the attempt ceiling is reached.
/// </summary>
public class ExecuteAccountDeletionCommandHandler
    : IRequestHandler<ExecuteAccountDeletionCommand, ErrorOr<Success>>
{
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserExternalLoginRepository _externalLoginRepository;
    private readonly IEnumerable<IExternalTokenLifecycle> _tokenLifecycles;
    private readonly IPerUserCryptoService _perUserCrypto;
    private readonly IImageStorageService _imageStorage;
    private readonly IPublisher _publisher;
    private readonly AccountDeletionSettings _settings;
    private readonly ILogger<ExecuteAccountDeletionCommandHandler> _logger;

    public ExecuteAccountDeletionCommandHandler(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        IUserExternalLoginRepository externalLoginRepository,
        IEnumerable<IExternalTokenLifecycle> tokenLifecycles,
        IPerUserCryptoService perUserCrypto,
        IImageStorageService imageStorage,
        IPublisher publisher,
        IOptions<AccountDeletionSettings> settings,
        ILogger<ExecuteAccountDeletionCommandHandler> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _externalLoginRepository = externalLoginRepository;
        _tokenLifecycles = tokenLifecycles;
        _perUserCrypto = perUserCrypto;
        _imageStorage = imageStorage;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ExecuteAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        var request = await _requestRepository.GetByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return AccountDeletionErrors.NotPendingGrace;
        }

        // Optimistic claim: the recovery flow flips PendingGrace→Cancelled the
        // same way, so exactly one of the two ever wins.
        var claimResult = request.Claim();
        if (claimResult.IsError)
        {
            return claimResult.Errors;
        }

        if (!await _requestRepository.UpdateAsync(request, AccountDeletionStatus.PendingGrace, cancellationToken))
        {
            _logger.LogInformation(
                "Deletion request {RequestId} was recovered before the claim; skipping", request.Id);
            return AccountDeletionErrors.NotPendingGrace;
        }

        var user = await _userRepository.GetByIdIncludeDeletedAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            // Already purged (a crash after the purge, or a restore-sweep
            // artifact): finish the bookkeeping idempotently, nothing to notify.
            request.Complete();
            await _requestRepository.UpdateAsync(request, AccountDeletionStatus.Processing, cancellationToken);
            return Result.Success;
        }

        // Pre-destruction snapshots: their only remaining use is the final
        // notification and the destruction log.
        var email = user.Email;
        var displayName = AccountDeletionRequestor.DisplayNameOf(user);
        var profileImageUrl = user.ProfileImageUrl;

        try
        {
            // Stage (d): revoke tokens at external identity providers (Apple)
            // while the stored refresh tokens and the DEK that unlocks them
            // still exist. Failures ride the request's retry budget; on the
            // final attempt the deletion proceeds regardless (provider tokens
            // expire on their own) with the outcome flagged in the audit.
            var externalRevocationOk = await RevokeExternalTokensAsync(user.Id, cancellationToken);
            if (!externalRevocationOk && request.AttemptCount < _settings.MaxExecutionAttempts - 1)
            {
                throw new InvalidOperationException(
                    "External provider token revocation failed; the deletion will retry.");
            }

            var purged = await _userRepository.HardDeleteAsync(user.Id, cancellationToken);
            if (!purged)
            {
                throw new InvalidOperationException(
                    "Purge refused: the account row is no longer soft-deleted.");
            }

            // Best-effort file cleanup after the transaction: an orphaned
            // image never blocks the deletion (the store ignores missing keys).
            if (profileImageUrl is not null)
            {
                try
                {
                    await _imageStorage.DeleteImageAsync(profileImageUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete profile image for destroyed account {UserId}", user.Id);
                }
            }

            request.Complete();
            if (!await _requestRepository.UpdateAsync(request, AccountDeletionStatus.Processing, cancellationToken))
            {
                _logger.LogError(
                    "Deletion request {RequestId} diverged while completing; destruction evidence may be stale",
                    request.Id);
            }

            _logger.LogInformation(
                "Account {UserId} permanently destroyed (policy {PolicyVersion}, attempt {Attempt})",
                request.UserId, request.PolicyVersion, request.AttemptCount + 1);

            await _publisher.Publish(
                new AccountDeletionCompletedEvent(
                    request.UserId, email, displayName, request.PolicyVersion,
                    ExternalRevocationFailed: !externalRevocationOk),
                cancellationToken);

            return Result.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Account destruction failed for request {RequestId} (attempt {Attempt})",
                request.Id, request.AttemptCount + 1);

            // Exception text is a diagnostic, never PII: purge failures carry
            // SQL/infrastructure messages, and the entity truncates to fit.
            request.Fail(ex.Message, _settings.MaxExecutionAttempts);
            await _requestRepository.UpdateAsync(request, AccountDeletionStatus.Processing, cancellationToken);

            return AccountDeletionErrors.ExecutionFailed;
        }
    }

    /// <summary>
    /// Revokes every stored external-provider refresh token for the user.
    /// Successfully revoked tokens are cleared so a retried execution never
    /// re-revokes; providers without a lifecycle strategy are skipped.
    /// </summary>
    private async Task<bool> RevokeExternalTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var logins = await _externalLoginRepository.GetByUserIdAsync(userId, cancellationToken);
        var allRevoked = true;

        foreach (var login in logins.Where(l => !string.IsNullOrEmpty(l.ProviderRefreshTokenEnc)))
        {
            var lifecycle = _tokenLifecycles.FirstOrDefault(
                l => string.Equals(l.ProviderName, login.Provider, StringComparison.OrdinalIgnoreCase));
            if (lifecycle is null)
            {
                continue;
            }

            try
            {
                var refreshToken = await _perUserCrypto.DecryptAsync(
                    userId, login.ProviderRefreshTokenEnc!,
                    EncryptedFieldPurpose.ExternalProviderRefreshToken, cancellationToken);

                if (await lifecycle.RevokeAsync(refreshToken, cancellationToken))
                {
                    await _externalLoginRepository.UpdateProviderRefreshTokenAsync(
                        login.Id, null, cancellationToken);
                }
                else
                {
                    allRevoked = false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to revoke {Provider} token for user {UserId}", login.Provider, userId);
                allRevoked = false;
            }
        }

        return allRevoked;
    }
}
