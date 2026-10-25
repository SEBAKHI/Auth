using Auth.Application.Configuration;
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
/// has to remember.
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
    // first factor that already went through the mailbox (D-T35 A).
    private static readonly IReadOnlyList<string> RecoveryOnly = [TwoFactorMethodNames.Recovery];

    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IOptionsMonitor<EmailSettings> _emailSettings;
    private readonly ILogger<TwoFactorChallengeService> _logger;

    public TwoFactorChallengeService(
        ITwoFactorChallengeRepository challengeRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IJwtTokenService jwtTokenService,
        ICredentialHasher credentialHasher,
        ITwoFactorStateStore twoFactorStateStore,
        IOptionsMonitor<EmailSettings> emailSettings,
        ILogger<TwoFactorChallengeService> logger)
    {
        _challengeRepository = challengeRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _jwtTokenService = jwtTokenService;
        _credentialHasher = credentialHasher;
        _twoFactorStateStore = twoFactorStateStore;
        _emailSettings = emailSettings;
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

        return new IssuedTwoFactorChallenge(token, await MethodsAsync(user.Id, primaryMethod, cancellationToken));
    }

    /// <summary>
    /// The factors this sign-in may be completed with, read from the account's row:
    /// the authenticator app (and its recovery codes) whatever the first factor; the
    /// emailed code only after the password and while email is on, read now — and its
    /// recovery codes always, so the way out never depends on the mailbox.
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

        return EmailCodeProofStrategy.MayFollow(primaryMethod) && _emailSettings.CurrentValue.Enabled
            ? EmailCodeMethods
            : RecoveryOnly;
    }
}
