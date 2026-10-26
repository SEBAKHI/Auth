using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SignInWithEmailCode;

/// <summary>
/// The sign-in exit by emailed code (OI-215): checks the code, finds the account its
/// address belongs to, and finishes the sign-in — or says what must come first.
/// </summary>
/// <remarks>
/// The order is the contract, each step placed for a reason:
/// <list type="number">
/// <item>The code's shape, then the code under the row's lock: a wrong one is charged
/// and answered before anything about the address is read.</item>
/// <item>The account by the row's address. None: the registration's completion creates
/// it (nothing consumed). Waiting for deletion: the recovery step (nothing consumed).</item>
/// <item>The lock and the status, by the provider sign-in's rule: an emailed code proves
/// the mailbox, never the password, so it lifts no lock (owner decision G8). A lapsed
/// lock is repaired; a timed one admits only the owner's own device; the hard lock and
/// an administrator's lock refuse.</item>
/// <item>The code, spent: checked once more under the lock without a charge and stamped
/// consumed, so it signs in once.</item>
/// <item>The first confirmation (D-215-5 (a)): an address proven here for the first
/// time retires a password set before that proof, and every other credential of the
/// account.</item>
/// <item>The decision: a password account holding platform authority, or asked for full
/// proof, continues with the ordinary password sign-in; an account with the
/// authenticator app gets its challenge; an account whose second factor is the emailed
/// code asks for none (the code just proved that mailbox); otherwise the session.</item>
/// </list>
/// </remarks>
public class SignInWithEmailCodeCommandHandler : IRequestHandler<SignInWithEmailCodeCommand, ErrorOr<LoginResponse>>
{
    private readonly EmailCodeCheck _codeCheck;
    private readonly EmailCodeAccountResolver _accounts;
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly IUserRepository _userRepository;
    private readonly IAccountLockout _accountLockout;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly EmailCodeFullProofRule _fullProof;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<SignInWithEmailCodeCommandHandler> _logger;

    public SignInWithEmailCodeCommandHandler(
        EmailCodeCheck codeCheck,
        EmailCodeAccountResolver accounts,
        IPendingRegistrationRepository pendingRegistrations,
        IUserRepository userRepository,
        IAccountLockout accountLockout,
        ILoginAttemptRepository loginAttemptRepository,
        EmailCodeFullProofRule fullProof,
        ITwoFactorChallengeService twoFactorChallengeService,
        ICredentialRevocationService credentialRevocation,
        ILoginResponseBuilder loginResponseBuilder,
        IDomainEventDispatcher eventDispatcher,
        ILogger<SignInWithEmailCodeCommandHandler> logger)
    {
        _codeCheck = codeCheck;
        _accounts = accounts;
        _pendingRegistrations = pendingRegistrations;
        _userRepository = userRepository;
        _accountLockout = accountLockout;
        _loginAttemptRepository = loginAttemptRepository;
        _fullProof = fullProof;
        _twoFactorChallengeService = twoFactorChallengeService;
        _credentialRevocation = credentialRevocation;
        _loginResponseBuilder = loginResponseBuilder;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(SignInWithEmailCodeCommand request, CancellationToken cancellationToken)
    {
        // 1. The code: shape, then under the lock. Nothing below runs for a guess.
        var checkedRow = await _codeCheck.CheckAsync(request.PendingId, request.Otp, cancellationToken);
        if (checkedRow.IsError)
        {
            return checkedRow.Errors;
        }

        var row = checkedRow.Value;

        // 2. The account the address belongs to. The mailbox's holder has proven
        // themself, so the answer may say what the address is.
        var account = await _accounts.ResolveAsync(row.Email.Value, cancellationToken);
        if (account.IsError)
        {
            return account.Errors;
        }

        if (account.Value.Next.Step != EmailCodeSteps.SignIn)
        {
            return new LoginResponse { NextStep = account.Value.Next };
        }

        var user = account.Value.User!;

        // 3. The lock and the status, as they are after a repair: a concurrent failure
        // may have locked the row again in between.
        if (user.LockKind(DateTime.UtcNow) == AccountLockKind.Lapsed)
        {
            var repaired = await _accountLockout.RepairLapsedLockAsync(user.Id, cancellationToken);
            if (repaired is null)
            {
                // Deleted in between: nothing left to sign in to.
                return EmailVerificationErrors.InvalidOrExpiredOtp;
            }

            user = repaired;
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        var lockKind = user.LockKind(DateTime.UtcNow);
        if (lockKind is not (AccountLockKind.None or AccountLockKind.Lapsed))
        {
            var familiar = lockKind == AccountLockKind.Timed
                && await AuthenticationHelper.IsFamiliarSourceAsync(
                    _loginAttemptRepository, user.Id, request.IpAddress, request.DeviceId, cancellationToken);
            if (!familiar)
            {
                return user.LockRefusal(lockKind);
            }

            _accountLockout.ReportFamiliarAdmission(user.Id, lockKind, user.FailedLoginAttempts);
        }

        // 4. The code, spent. Two sign-ins with one code both reach this line; only
        // the first finds the row unconsumed.
        if (!await _pendingRegistrations.TryConsumeSignInCodeAsync(row.Id, request.Otp, cancellationToken))
        {
            _logger.LogWarning(
                "Sign-in by code refused: pending row {PendingRegistrationId} changed or was spent under the lock",
                row.Id);
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        // 5. The first confirmation of the address.
        if (!user.EmailConfirmed)
        {
            var confirmed = await ConfirmAddressAsync(user, cancellationToken);
            if (confirmed.IsError)
            {
                return confirmed.Errors;
            }

            user = confirmed.Value;
        }

        // 6. The decision. What the code proved: the mailbox, and for an account
        // without a password its first factor (D-215-1).
        var methods = AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: user.PasswordHash is not null);

        if (await _fullProof.RequiresPasswordAsync(user, request.RequireFullProof, cancellationToken))
        {
            _logger.LogInformation(
                "Sign-in by code of account {UserId} continues with its password", user.Id);
            return new LoginResponse { NextStep = EmailCodeNextStep.PasswordRequiredToFinish };
        }

        if (user.TwoFactorEnabled)
        {
            if (await _twoFactorChallengeService.SignInCodeProvesFactorAsync(user, cancellationToken))
            {
                _logger.LogInformation(
                    "{EventCode}: no second step for the sign-in by code of account {UserId}: the code proved the mailbox its second factor would",
                    TwoFactorSignalCodes.SignInCodeMailboxWaived, user.Id);
            }
            else
            {
                var challenge = await _twoFactorChallengeService.CreateChallengeAsync(
                    user, request.IpAddress, request.UserAgent, methods, cancellationToken);

                _logger.LogInformation(
                    "Two-factor verification pending for the sign-in by code of user {UserId}", user.Id);

                return challenge.ToPendingLogin();
            }
        }

        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, methods,
            passwordProven: false, cancellationToken, persistent: request.StaySignedIn);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit: the pending UserLoggedInEvent is dropped.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return loginResponse;
    }

    /// <summary>
    /// The address proven for the first time (D-215-5 (a)): confirmed, a password set
    /// before the proof removed in the same statement, and every other credential of
    /// the account revoked through the revocation service, before this sign-in mints
    /// its own. The removal is announced at once: the sign-in may stop at a challenge
    /// below, and the owner must hear of it either way.
    /// </summary>
    private async Task<ErrorOr<User>> ConfirmAddressAsync(User user, CancellationToken cancellationToken)
    {
        var readHash = user.PasswordHash;
        var removed = user.ConfirmEmailBySignInCode(user.Id);

        if (!await _userRepository.ConfirmEmailBySignInCodeAsync(user.Id, readHash, cancellationToken))
        {
            // Confirmed, or given another password, between the read and the write:
            // judge the account as it is now.
            var current = await _userRepository.GetByIdAsync(user.Id, cancellationToken);
            if (current is null || !current.EmailConfirmed)
            {
                return EmailVerificationErrors.InvalidOrExpiredOtp;
            }

            return current;
        }

        _logger.LogInformation(
            removed
                ? "Address of user {UserId} confirmed by a sign-in code; the password set before it was removed"
                : "Address of user {UserId} confirmed by a sign-in code",
            user.Id);

        if (removed)
        {
            // Whoever set that password may hold a session or a refresh token
            // through it. Never the session repository alone: that evicts nobody.
            await _credentialRevocation.RevokeCredentialsAsync(
                user.Id,
                exceptSessionId: null,
                exceptIdpSessionToken: null,
                revokedBy: user.Id,
                "Password set before the address was proven was removed",
                cancellationToken);
        }

        try
        {
            await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Events of the first confirmation of user {UserId} were not dispatched", user.Id);
        }

        return user;
    }
}
