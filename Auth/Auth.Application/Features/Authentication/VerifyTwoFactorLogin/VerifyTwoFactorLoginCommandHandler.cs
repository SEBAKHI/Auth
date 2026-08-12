using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Handler that completes a two-factor login: validates the pending challenge,
/// verifies the TOTP or recovery code, and only then issues tokens via the
/// shared login response builder.
/// </summary>
public class VerifyTwoFactorLoginCommandHandler : IRequestHandler<VerifyTwoFactorLoginCommand, ErrorOr<LoginResponse>>
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ITwoFactorAuthRepository _twoFactorRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ITotpService _totpService;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<VerifyTwoFactorLoginCommandHandler> _logger;

    public VerifyTwoFactorLoginCommandHandler(
        ITwoFactorChallengeRepository challengeRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ITotpService totpService,
        ILoginResponseBuilder loginResponseBuilder,
        IDomainEventDispatcher eventDispatcher,
        ILogger<VerifyTwoFactorLoginCommandHandler> logger)
    {
        _challengeRepository = challengeRepository;
        _twoFactorRepository = twoFactorRepository;
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _totpService = totpService;
        _loginResponseBuilder = loginResponseBuilder;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        VerifyTwoFactorLoginCommand request,
        CancellationToken cancellationToken)
    {
        // Locate the pending challenge by the hash of the presented token.
        // Not-found, expired, used, and attempts-exhausted all map to the same
        // opaque error so the endpoint is not an oracle.
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(request.ChallengeToken);
        var challenge = await _challengeRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (challenge == null || !challenge.IsValid)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        var user = await _userRepository.GetByIdAsync(challenge.UserId, cancellationToken);
        if (user == null)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        // Account state may have changed within the challenge window.
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        if (user.IsLockedOut())
        {
            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        var twoFactor = await _twoFactorRepository.GetByUserIdAsync(challenge.UserId, cancellationToken);
        if (twoFactor == null || !twoFactor.IsEnabled)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        if (twoFactor.IsLocked)
        {
            return TwoFactorErrors.LockedOut;
        }

        var verification = request.UseRecoveryCode
            ? VerifyRecoveryCode(twoFactor, request.Code)
            : VerifyTotpCode(twoFactor, request.Code);

        if (verification.IsError)
        {
            await _challengeRepository.IncrementAttemptCountAsync(challenge.Id, cancellationToken);

            twoFactor.RecordFailure();
            await _twoFactorRepository.UpdateAsync(twoFactor, cancellationToken);

            // A rejected code does not end the ceremony, so it does not write a row
            // of its own — the count is kept on the challenge and surfaces in the
            // history alongside the one row this sign-in owns. Only exhausting the
            // allowance ends it, and that is the outcome worth recording.
            challenge.IncrementAttempts();
            if (!challenge.IsValid)
            {
                await _loginAttemptRepository.ResolveTwoFactorCeremonyAsync(
                    challenge.Id, false, "Too many incorrect verification codes", cancellationToken);
            }

            _logger.LogWarning(
                "Failed two-factor verification for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return verification.Errors;
        }

        // Single-use: consume the challenge before issuing tokens.
        var markUsed = challenge.MarkAsUsed();
        if (markUsed.IsError)
        {
            return markUsed.Errors;
        }

        // The in-memory check above was read from a snapshot, so two requests
        // arriving together both pass it. The database claim is what actually
        // decides: whoever loses it gets nothing, and must lose it here — before
        // the success is recorded and before any token is minted.
        if (!await _challengeRepository.MarkAsUsedAsync(challenge.Id, cancellationToken))
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        twoFactor.RecordSuccess();
        await _twoFactorRepository.UpdateAsync(twoFactor, cancellationToken);

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        // The challenge id travels with the build so the success — or a late
        // session-limit refusal — settles the row this ceremony already owns
        // instead of appending a second one.
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken,
            twoFactorChallengeId: challenge.Id);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit. The challenge has already been
            // consumed above and stays consumed — the code was correct, and
            // replaying it must not become a way around the limit.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        _logger.LogInformation(
            "Two-factor login completed for user {UserId} from {IpAddress}",
            user.Id, request.IpAddress);

        return loginResponse;
    }

    private ErrorOr<Success> VerifyTotpCode(TwoFactorAuth twoFactor, string code)
    {
        if (!_totpService.ValidateCode(twoFactor.SecretKey, code))
        {
            return UserErrors.InvalidTwoFactorCode;
        }

        return Result.Success;
    }

    /// <summary>
    /// Verifies a recovery code against the stored hashes and consumes the
    /// matched code so it cannot be reused.
    /// </summary>
    private ErrorOr<Success> VerifyRecoveryCode(TwoFactorAuth twoFactor, string code)
    {
        if (string.IsNullOrWhiteSpace(twoFactor.RecoveryCodes))
        {
            return TwoFactorErrors.NoRecoveryCodesAvailable;
        }

        var hashes = JsonSerializer.Deserialize<List<string>>(twoFactor.RecoveryCodes);
        if (hashes == null || hashes.Count == 0)
        {
            return TwoFactorErrors.NoRecoveryCodesAvailable;
        }

        var matched = hashes.FirstOrDefault(hash => _totpService.VerifyRecoveryCode(code, hash));
        if (matched == null)
        {
            return TwoFactorErrors.InvalidRecoveryCode;
        }

        hashes.Remove(matched);
        twoFactor.UpdateRecoveryCodes(JsonSerializer.Serialize(hashes));

        return Result.Success;
    }
}
