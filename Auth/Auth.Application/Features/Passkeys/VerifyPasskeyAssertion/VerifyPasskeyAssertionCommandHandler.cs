using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;

/// <summary>
/// Handler that signs in with a passkey (S20 PR B, PK8, FA4 (4-4), FA5 (5-1), FA6).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Steps 1–7, shared with the passkey recovery (<see cref="PasskeyAssertionCeremony"/>): the
/// cookie, the challenge spent, the credential found, the user handle compared, the library's
/// check, the counter and its conditional record.</item>
/// <item>FA6, on the owner as the sign-in reads it (<see cref="IUserRepository.GetByIdAsync"/>): an
/// account waiting for deletion answers with the end of its grace period, as the password
/// sign-in does, so the page can offer the passkey recovery; any other deleted account — an
/// administrator's deletion included — is refused as a failed ceremony, with no session; then the
/// status (inactive, pending) and the lock (<see cref="PasskeyLockAdmission"/>).</item>
/// <item>No second step: a passkey the device verified the person for proves two factors (M3).</item>
/// <item>The session: the methods recorded on the account and given to the builder, which does
/// not lift a lock (<c>passwordProven: false</c>, G8); then the events.</item>
/// </list>
/// No sign-in row is written for a refusal (FA4 (4-4)): a passkey has no failure count, and the
/// rows a password sign-in writes feed the per-address ceiling of wrong passwords. Not asked: the
/// refusal of a passkey-only administrator (<see cref="PasskeyOnlyAdministratorRule"/>), since
/// this is the passkey's own door.
/// </remarks>
public sealed class VerifyPasskeyAssertionCommandHandler
    : IRequestHandler<VerifyPasskeyAssertionCommand, ErrorOr<LoginResponse>>
{
    private readonly PasskeyAssertionCeremony _ceremony;
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _deletionRequestRepository;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VerifyPasskeyAssertionCommandHandler> _logger;

    public VerifyPasskeyAssertionCommandHandler(
        PasskeyAssertionCeremony ceremony,
        IUserRepository userRepository,
        IAccountDeletionRequestRepository deletionRequestRepository,
        ILoginResponseBuilder loginResponseBuilder,
        IDomainEventDispatcher eventDispatcher,
        TimeProvider timeProvider,
        ILogger<VerifyPasskeyAssertionCommandHandler> logger)
    {
        _ceremony = ceremony;
        _userRepository = userRepository;
        _deletionRequestRepository = deletionRequestRepository;
        _loginResponseBuilder = loginResponseBuilder;
        _eventDispatcher = eventDispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        VerifyPasskeyAssertionCommand request,
        CancellationToken cancellationToken)
    {
        // 1-7. The assertion.
        var proven = await _ceremony.ProveAsync(
            request.ChallengeHandle, request.ChallengeHandleRepeated, request.ResponseJson, cancellationToken);
        if (proven.IsError)
        {
            return proven.Errors;
        }

        var passkey = proven.Value;

        // 8. FA6: the owner as a sign-in reads it — never a deleted account.
        var owner = await _userRepository.GetByIdAsync(passkey.UserId, cancellationToken);
        if (owner is null)
        {
            return await AbsentOwnerAsync(passkey, cancellationToken);
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(owner);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        if (PasskeyLockAdmission.Judge(owner, _timeProvider.GetUtcNow().UtcDateTime, _logger) is { } lockRefusal)
        {
            return lockRefusal;
        }

        // 9. No second step (M3). 10. The session, which lifts no lock.
        var methods = AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);
        owner.RecordSuccessfulLogin(methods, request.IpAddress, request.UserAgent);

        var loginResponse = await _loginResponseBuilder.BuildAsync(
            owner, request.IpAddress, request.UserAgent, request.DeviceId, methods,
            passwordProven: false, cancellationToken, persistent: request.StaySignedIn);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit: the pending UserLoggedInEvent is dropped.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(owner, cancellationToken);

        _logger.LogInformation(
            "User {UserId} signed in with passkey {PasskeyId}", owner.Id, passkey.Id);

        return loginResponse;
    }

    /// <summary>
    /// The owner the sign-in cannot read: deleted. One waiting for deletion learns when its grace
    /// period ends — the passkey proved the person, as the password does on the password sign-in
    /// — so the page can offer the recovery; any other is refused as a failed ceremony.
    /// </summary>
    private async Task<Error> AbsentOwnerAsync(UserPasskey passkey, CancellationToken cancellationToken)
    {
        var deleted = await _userRepository.GetByIdIncludeDeletedAsync(passkey.UserId, cancellationToken);
        if (deleted is { IsDeleted: true })
        {
            var deletion = await _deletionRequestRepository.GetActiveByUserIdAsync(deleted.Id, cancellationToken);
            if (deletion is { Status: AccountDeletionStatus.PendingGrace })
            {
                return UserErrors.AccountPendingDeletion(deletion.GraceEndsAtUtc);
            }
        }

        _logger.LogInformation(
            "{EventCode}: a passkey sign-in with passkey {PasskeyId} of user {UserId} was refused: the account is deleted",
            PasskeySignalCodes.VerificationFailed, passkey.Id, passkey.UserId);
        return PasskeyErrors.VerificationFailed;
    }
}
