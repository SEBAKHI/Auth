using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;

/// <summary>
/// Handler of the passkey entry of the grace-period recovery (S20 PR B, D-S20-6 (a)).
/// </summary>
/// <remarks>
/// <para>
/// Why it exists: during the grace period only the person's own recovery cancels a deletion,
/// and an administrator whose only usable factor is a passkey is refused at the password, the
/// provider and the emailed-code entries (D-S20-4 (ii-s)). Without this entry such an
/// administrator's account would be deleted when the grace period ends. It serves every account
/// with a passkey, not administrators alone.
/// </para>
/// <list type="number">
/// <item>The passkey sign-in's own steps (<see cref="PasskeyAssertionCeremony"/>), shared, never
/// copied: every failure has spent the challenge, as at the sign-in.</item>
/// <item>The owner must be waiting for deletion: soft-deleted, with a request in its grace period.
/// Anything else — a live account, an unknown one, an administrator's deletion with no request —
/// answers what the password entry answers for it, <c>User.InvalidCredentials</c>, and the caller
/// learns nothing more.</item>
/// <item>The lock, as at the sign-in (<see cref="PasskeyLockAdmission"/>): an administrator's lock
/// refuses; a lock raised by wrong passwords admits and lifts nothing.</item>
/// <item>The shared pipeline (<see cref="AccountDeletionRecoverer"/>): with the passkey's methods,
/// which satisfy two factors, so no second step is asked; it cancels the request, restores the
/// account, publishes the cancellation and builds a session that does not lift a lock
/// (<c>passwordProven: false</c>).</item>
/// </list>
/// </remarks>
public sealed class RecoverAccountWithPasskeyCommandHandler
    : IRequestHandler<RecoverAccountWithPasskeyCommand, ErrorOr<LoginResponse>>
{
    private readonly PasskeyAssertionCeremony _ceremony;
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly AccountDeletionRecoverer _recoverer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecoverAccountWithPasskeyCommandHandler> _logger;

    public RecoverAccountWithPasskeyCommandHandler(
        PasskeyAssertionCeremony ceremony,
        IUserRepository userRepository,
        IAccountDeletionRequestRepository requestRepository,
        AccountDeletionRecoverer recoverer,
        TimeProvider timeProvider,
        ILogger<RecoverAccountWithPasskeyCommandHandler> logger)
    {
        _ceremony = ceremony;
        _userRepository = userRepository;
        _requestRepository = requestRepository;
        _recoverer = recoverer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        RecoverAccountWithPasskeyCommand request,
        CancellationToken cancellationToken)
    {
        // 1. The passkey sign-in's own steps.
        var proven = await _ceremony.ProveAsync(
            request.ChallengeHandle, request.ChallengeHandleRepeated, request.ResponseJson, cancellationToken);
        if (proven.IsError)
        {
            return proven.Errors;
        }

        var passkey = proven.Value;

        // 2. An account waiting for deletion, and nothing else.
        var user = await _userRepository.GetByIdIncludeDeletedAsync(passkey.UserId, cancellationToken);
        if (user is not { IsDeleted: true })
        {
            return UserErrors.InvalidCredentials;
        }

        var deletionRequest = await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (deletionRequest is not { Status: AccountDeletionStatus.PendingGrace })
        {
            return UserErrors.InvalidCredentials;
        }

        // 3. The lock, as at the sign-in.
        if (PasskeyLockAdmission.Judge(user, _timeProvider.GetUtcNow().UtcDateTime, _logger) is { } lockRefusal)
        {
            return lockRefusal;
        }

        // 4. The shared pipeline: no second step for a passkey (M3), no lock lifted.
        _logger.LogInformation(
            "Recovery of account {UserId} with passkey {PasskeyId}", user.Id, passkey.Id);

        return await _recoverer.RecoverAsync(
            user,
            deletionRequest,
            AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified),
            twoFactorCode: null,
            request.IpAddress,
            request.UserAgent,
            request.DeviceId,
            cancellationToken);
    }
}
