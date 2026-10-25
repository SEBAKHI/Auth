using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.DisableEmailCode;

/// <summary>
/// Handler for the command that switches the emailed second factor off (S24, EO8).
/// </summary>
/// <remarks>
/// <para>
/// Asks for more than the session, as switching the authenticator app off does: a
/// recent sign-in first, then a proof — a code emailed for this purpose, or a recovery
/// code for an owner whose mailbox is out of reach (<see cref="EmailCodeFactorProof"/>).
/// Without a code the request emails one and answers where it went.
/// </para>
/// <para>
/// The emailed code never satisfies the platform-administrator policy, so switching it
/// off takes nothing from it: no administrator is refused here.
/// </para>
/// <para>
/// The proof, the factor row and the account flag change together; the other sessions
/// are signed out, and the owner is told by email.
/// </para>
/// </remarks>
public class DisableEmailCodeCommandHandler : IRequestHandler<DisableEmailCodeCommand, ErrorOr<DisableEmailCodeResult>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IUserRepository _userRepository;
    private readonly EmailCodeFactorProof _factorProof;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<DisableEmailCodeCommandHandler> _logger;

    public DisableEmailCodeCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        ITwoFactorStateStore twoFactorStateStore,
        IUserRepository userRepository,
        EmailCodeFactorProof factorProof,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        ILogger<DisableEmailCodeCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _twoFactorStateStore = twoFactorStateStore;
        _userRepository = userRepository;
        _factorProof = factorProof;
        _credentialRevocation = credentialRevocation;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<DisableEmailCodeResult>> Handle(
        DisableEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        // 1. A recent sign-in, before anything is read, counted, sent or written.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // 2. Only an account whose factor is the emailed code has one to switch off.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot?.HasEmailCode != true)
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 3. No code: email one.
        if (string.IsNullOrEmpty(request.Code))
        {
            var sent = await _factorProof.SendAsync(user, request.IpAddress, cancellationToken);
            return sent.IsError ? sent.Errors : new DisableEmailCodeResult(sent.Value);
        }

        // 4. The proof, counted before it is checked.
        var proof = await _factorProof.VerifyAsync(
            request.UserId, request.Code, request.UseRecoveryCode, cancellationToken);
        if (proof.IsError)
        {
            _logger.LogWarning(
                "A wrong code was presented to switch the emailed second factor off for user {UserId}",
                request.UserId);
            return proof.Errors;
        }

        // 5. The proof spent, the row and the flag, in one transaction.
        var outcome = await _twoFactorStateStore.TryRemoveEmailCodeAsync(request.UserId, proof.Value, cancellationToken);

        switch (outcome)
        {
            case LoginCommitOutcome.Committed:
                break;

            case LoginCommitOutcome.ChallengeLost:
                // A newer code was sent, or another request spent this one, first.
                return TwoFactorErrors.RecoveryEmailCodeRequired;

            case LoginCommitOutcome.RecoveryCodesChanged:
                // A concurrent request spent a code between the check and the commit.
                return TwoFactorErrors.InvalidRecoveryCode;

            default:
                // The emailed code was switched off or replaced meanwhile — or an
                // outcome this handler does not know, which never switches anything off.
                return UserErrors.TwoFactorNotEnabled;
        }

        // 6. Recorded on the aggregate only now, for a change that happened.
        user.DisableEmailCodeFactor(request.UserId, session.Value.DeviceName);

        _logger.LogInformation(
            "The emailed second factor was switched off for user {UserId}",
            request.UserId);

        // 7. Sign out every other session and browser before the events run, as
        //    switching the authenticator app off does. The factor is already off, so
        //    neither this nor the notice may be undone by a client that disconnects,
        //    and a failed revocation must not cost the owner the email.
        await _credentialRevocation.RevokeOtherSessionsAsync(
            request.UserId, request.CurrentSessionId, request.IdpSessionToken,
            "Emailed second factor disabled", _logger);

        // 8. The audit row and the email to the owner.
        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return new DisableEmailCodeResult(CodeSent: null);
    }
}
