using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.EnableEmailCode;

/// <summary>
/// Handler for the command that starts binding the emailed code as the second factor.
/// </summary>
/// <remarks>
/// <para>
/// Everything that would make the bind fail is checked before any email goes out, in
/// the order nothing is counted: a recent sign-in (X02); email sending on, read for
/// this request; a password on the account (the emailed code is the step that follows
/// a password, and an account without one has none for it to follow); a confirmed
/// address; the emailed code not bound yet.
/// </para>
/// <para>
/// Over an authenticator app the emailed code REPLACES it (the two are never bound
/// together, D-T34 B), so two more rules apply: the session must have proved two
/// factors (D-T31 B) — otherwise whoever holds the password could swap the owner's app
/// for the mailbox — and a platform administrator under enforcement may not give up
/// the one factor the policy counts (<c>TwoFactor.RequiredByPolicy</c>).
/// </para>
/// <para>
/// Then a code goes to the address on the account's live enrollment challenge, opened
/// when none is live; the sender holds every limit.
/// </para>
/// </remarks>
public class EnableEmailCodeCommandHandler : IRequestHandler<EnableEmailCodeCommand, ErrorOr<EmailCodeSent>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly AccountEmailCodeChallenge _accountChallenge;
    private readonly IUserRepository _userRepository;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly ISecondFactorLockout _lockout;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<EnableEmailCodeCommandHandler> _logger;

    public EnableEmailCodeCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IPlatformMfaPolicy platformMfaPolicy,
        ITwoFactorStateStore twoFactorStateStore,
        AccountEmailCodeChallenge accountChallenge,
        IUserRepository userRepository,
        IEmailCodeSender emailCodeSender,
        ISecondFactorLockout lockout,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<EnableEmailCodeCommandHandler> logger)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _platformMfaPolicy = platformMfaPolicy;
        _twoFactorStateStore = twoFactorStateStore;
        _accountChallenge = accountChallenge;
        _userRepository = userRepository;
        _emailCodeSender = emailCodeSender;
        _lockout = lockout;
        // A snapshot, read in this request's scope: Email:Enabled is hot.
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<EmailCodeSent>> Handle(
        EnableEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        // A recent sign-in, before anything is read, counted or written.
        var session = await _reauthenticationGuard.EnsureRecentSignInAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        // Email off: no code could be sent or confirmed, so nothing is opened.
        if (!_emailSettings.Enabled)
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        if (user.PasswordHash is null)
        {
            return TwoFactorErrors.EmailCodeRequiresPassword;
        }

        // A code goes only to an address someone proved.
        if (!user.EmailConfirmed)
        {
            return TwoFactorErrors.EmailCodeRecipientUnavailable;
        }

        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);
        if (snapshot?.HasEmailCode == true)
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        if (snapshot?.IsEnabled == true)
        {
            var replacement = await AuthenticatorReplacementRule.EnsureAllowedAsync(
                _platformMfaPolicy, request.UserId, session.Value, cancellationToken);
            if (replacement.IsError)
            {
                return replacement.Errors;
            }
        }

        var challenge = await _accountChallenge.GetOrOpenAsync(
            request.UserId, TwoFactorChallengePurpose.EmailEnrollment, primaryMethod: default, request.IpAddress, cancellationToken);

        var sent = await _emailCodeSender.SendAsync(
            user, challenge, request.IpAddress, _lockout.HardLimit, cancellationToken);

        if (!sent.IsError)
        {
            _logger.LogInformation(
                "A code to bind the emailed second factor was sent for user {UserId}",
                request.UserId);
        }

        return sent;
    }
}
