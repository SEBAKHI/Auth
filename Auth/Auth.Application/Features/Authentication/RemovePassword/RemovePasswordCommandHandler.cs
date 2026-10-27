using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.RemovePassword;

/// <summary>
/// Removes an account's password at its owner's request (OI-215, D-215-3), under the
/// owner's six conditions and the seventh of D-110-1.
/// </summary>
/// <remarks>
/// <para>
/// Refused unless all hold, in this order, each refusal its own code so the screen can
/// say what to do:
/// </para>
/// <list type="number">
/// <item>There is a password, email sending is on, and the address is confirmed: the
/// account will sign in only with a code emailed to that address (conditions 2).</item>
/// <item>The emailed code is not the account's second factor: after a sign-in by
/// emailed code it would prove the same mailbox again. Switch it off, or use the
/// authenticator app, first.</item>
/// <item>An account that holds any platform permission has its authenticator app
/// bound (D-110-1, the seventh condition): without a password and without the app,
/// the mailbox alone would open an account that administers the platform.</item>
/// <item>A recent sign-in that proved the password, and the authenticator app when it
/// is bound (condition 1). An emailed code never suffices: whoever holds only the
/// mailbox must not be able to turn an account code-only quietly.</item>
/// </list>
/// <para>
/// The confirmation (condition 3) is the screen's. Then, in one transaction, the hash
/// is removed (only over the one that was read) and joins the password history; the
/// account's other credentials are revoked through the revocation service, always,
/// whatever <c>Session:TerminateSessionsOnPasswordChange</c> says, because the owner
/// listed it as a condition (4), and the current session is kept; the event is audited
/// and emailed to the owner (4, 5). An account holding platform authority may do it
/// too (6), once its authenticator app is bound (7).
/// </para>
/// </remarks>
public class RemovePasswordCommandHandler : IRequestHandler<RemovePasswordCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly EmailCodeFullProofRule _fullProof;
    private readonly EmailSettings _emailSettings;
    private readonly PasswordSettings _passwordSettings;
    private readonly ILogger<RemovePasswordCommandHandler> _logger;

    public RemovePasswordCommandHandler(
        IUserRepository userRepository,
        ITwoFactorStateStore twoFactorStateStore,
        IReauthenticationGuard reauthenticationGuard,
        ICredentialRevocationService credentialRevocation,
        IPasswordHistoryRepository passwordHistoryRepository,
        IDomainEventDispatcher eventDispatcher,
        EmailCodeFullProofRule fullProof,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<RemovePasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _reauthenticationGuard = reauthenticationGuard;
        _credentialRevocation = credentialRevocation;
        _passwordHistoryRepository = passwordHistoryRepository;
        _eventDispatcher = eventDispatcher;
        _fullProof = fullProof;
        _emailSettings = emailSettings.Value;
        _passwordSettings = passwordSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(RemovePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 1. Something to remove, and a way back in without it.
        if (user.PasswordHash is null)
        {
            return UserErrors.PasswordNotSet;
        }

        if (!_emailSettings.Enabled)
        {
            return UserErrors.PasswordRemovalRequiresEmail;
        }

        if (!user.EmailConfirmed)
        {
            return UserErrors.EmailNotConfirmed;
        }

        // 2. The emailed second factor would follow an emailed code.
        var factor = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);
        if (factor?.HasEmailCode == true)
        {
            return UserErrors.PasswordRemovalBlockedByEmailCodeFactor;
        }

        // 2b. Platform authority keeps a second proof the mailbox cannot give
        // (D-110-1): the authenticator app, bound.
        if (factor?.IsEnabled != true && await _fullProof.HoldsPlatformAuthorityAsync(user.Id, cancellationToken))
        {
            _logger.LogInformation(
                "Password removal for user {UserId} refused: the account holds platform permissions and has no authenticator app",
                user.Id);
            return UserErrors.PasswordRemovalRequiresAuthenticatorApp;
        }

        // 3. A recent sign-in that proved the password itself, and the app when it is
        // bound. The guard's refusal asks the person to sign in again.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            user.Id, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var methods = session.Value.Methods;
        if (!methods.ProvesPassword || (factor?.IsEnabled == true && !methods.IsMfaSatisfied))
        {
            _logger.LogInformation(
                "Password removal for user {UserId} asked for a sign-in that proves the password (methods {AuthMethods})",
                user.Id, methods.Value);
            return AuthErrors.ReauthenticationRequired;
        }

        var readHash = user.PasswordHash;
        var removal = user.RemovePassword(user.Id);
        if (removal.IsError)
        {
            return removal.Errors;
        }

        if (!await _userRepository.RemovePasswordAsync(user.Id, readHash, user.Id, cancellationToken))
        {
            // The password changed between the read and the write (a reset, another
            // tab): nothing was removed, and the person is asked to sign in again
            // with the password the account has now.
            _logger.LogWarning("Password removal for user {UserId} found the password changed under it", user.Id);
            return AuthErrors.ReauthenticationRequired;
        }

        await _passwordHistoryRepository.CleanupOldHistoryAsync(
            user.Id, _passwordSettings.HistoryCount, cancellationToken);

        // Always, and through the revocation service: ending UserSessions rows alone
        // evicts nobody.
        await _credentialRevocation.RevokeCredentialsAsync(
            user.Id,
            request.CurrentSessionId,
            request.IdpSessionToken,
            revokedBy: user.Id,
            "Password removed",
            cancellationToken);

        _logger.LogInformation(
            "Password removed by user {UserId}; other credentials revoked, current session kept", user.Id);

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return Result.Success;
    }
}
