using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared service that creates login-time two-factor challenges. Every gate that
/// can demand a second factor goes through here — password login, external
/// provider login, and email verification — which is what makes "one open
/// ceremony row per issued challenge" structural rather than a rule each handler
/// has to remember. So is "a challenge that offers the emailed code has emailed
/// it" (D-243): the code is sent where the methods are decided.
/// </summary>
public class TwoFactorChallengeService : ITwoFactorChallengeService
{
    // The authenticator app's sign-in: its code, or a recovery code — whatever the
    // first factor was.
    private static readonly IReadOnlyList<string> AuthenticatorMethods =
        [TwoFactorMethodNames.Totp, TwoFactorMethodNames.Recovery];

    // The emailed code's sign-in after the password, while email is on.
    private static readonly IReadOnlyList<string> EmailCodeMethods =
        [TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery];

    // The emailed code's sign-in when no code can be sent for it: email off, or a
    // verify-email link, which already went through the mailbox.
    private static readonly IReadOnlyList<string> RecoveryOnly = [TwoFactorMethodNames.Recovery];

    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IOptionsMonitor<EmailSettings> _emailSettings;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly ISecondFactorLockout _lockout;
    private readonly ILogger<TwoFactorChallengeService> _logger;

    public TwoFactorChallengeService(
        ITwoFactorChallengeRepository challengeRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IJwtTokenService jwtTokenService,
        ICredentialHasher credentialHasher,
        ITwoFactorStateStore twoFactorStateStore,
        IOptionsMonitor<EmailSettings> emailSettings,
        IEmailCodeSender emailCodeSender,
        ISecondFactorLockout lockout,
        ILogger<TwoFactorChallengeService> logger)
    {
        _challengeRepository = challengeRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _jwtTokenService = jwtTokenService;
        _credentialHasher = credentialHasher;
        _twoFactorStateStore = twoFactorStateStore;
        _emailSettings = emailSettings;
        _emailCodeSender = emailCodeSender;
        _lockout = lockout;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IssuedTwoFactorChallenge> CreateChallengeAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        AuthenticationMethods primaryMethod,
        CancellationToken cancellationToken)
    {
        // High-entropy opaque token; reuses the CSPRNG refresh-token generator.
        var token = _jwtTokenService.GenerateRefreshToken();
        var tokenHash = _credentialHasher.Hash(CredentialPurpose.TwoFactorChallenge, token);

        // A new login supersedes any pending challenge for the same user.
        await _challengeRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var challenge = TwoFactorChallenge.Create(user.Id, tokenHash, ipAddress, primaryMethod: primaryMethod);
        await _challengeRepository.CreateAsync(challenge, cancellationToken);

        // Open the ceremony. Nothing else will write a row for this sign-in: the
        // verify step settles this one. If the code never arrives, the row stays
        // open and that IS the record — somebody produced the correct password and
        // went no further, which is the single most useful thing this table holds.
        await _loginAttemptRepository.CreateAsync(
            LoginAttempt.CreateChallenged(user.Id, user.Email, challenge.Id, ipAddress, userAgent),
            cancellationToken);

        _logger.LogInformation(
            "Two-factor challenge issued for user {UserId} from {IpAddress}",
            user.Id, ipAddress);

        var methods = await MethodsAsync(user.Id, primaryMethod, cancellationToken);

        // The emailed code goes out with the challenge, once (D-243): nothing else
        // sends on its own, and the client's button is the resend.
        var emailCode = methods?.Contains(TwoFactorMethodNames.Email) == true
            ? await SendEmailCodeAsync(user, challenge, ipAddress, cancellationToken)
            : null;

        return new IssuedTwoFactorChallenge(token, methods, emailCode);
    }

    /// <inheritdoc />
    public async Task<bool> ProviderProvesMailboxAsync(
        User user,
        ExternalUserInfo provider,
        CancellationToken cancellationToken)
    {
        // The provider's own address, normalized as every address is (trimmed, lower
        // case): no dot or plus folding, so only the very address matches.
        if (!provider.ControlsMailbox
            || Email.Create(provider.Email) is not { IsError: false } address
            || address.Value != user.Email)
        {
            return false;
        }

        // The emailed code alone: a row holding the authenticator app (or both) keeps
        // its challenge, since the app proves something no mailbox does.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);
        return snapshot is { HasEmailCode: true, IsEnabled: false };
    }

    /// <summary>
    /// Emails the challenge's first code through the one sender, under its limits.
    /// Never fails the sign-in: a refusal is logged by the sender with its signal, and
    /// a fault here; either way the answer carries no code and the client asks for one.
    /// </summary>
    private async Task<EmailCodeSent?> SendEmailCodeAsync(
        User user,
        TwoFactorChallenge challenge,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _emailCodeSender.SendAsync(
                user, challenge, ipAddress, _lockout.HardLimit, cancellationToken);
            return sent.IsError ? null : sent.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "The emailed code for challenge {ChallengeId} of account {UserId} could not be sent; the sign-in answers without it",
                challenge.Id, user.Id);
            return null;
        }
    }

    /// <summary>
    /// The factors this sign-in may be completed with, read from the account's row:
    /// the authenticator app (and its recovery codes) whatever the first factor; the
    /// emailed code after the password or a provider (D-243) and while email is on,
    /// read now — and its recovery codes always, so the way out never depends on the
    /// mailbox.
    /// </summary>
    private async Task<IReadOnlyList<string>?> MethodsAsync(
        Guid userId,
        AuthenticationMethods primaryMethod,
        CancellationToken cancellationToken)
    {
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(userId, cancellationToken);
        if (snapshot is null)
        {
            // The account flag said "challenge", but there is no factor row: the
            // verify step will say so; nothing is offered here.
            return null;
        }

        if (!snapshot.HasEmailCode)
        {
            return AuthenticatorMethods;
        }

        return EmailCodeProofStrategy.MayFollowAtSignIn(primaryMethod) && _emailSettings.CurrentValue.Enabled
            ? EmailCodeMethods
            : RecoveryOnly;
    }
}
