using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
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
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ILogger<TwoFactorChallengeService> _logger;

    public TwoFactorChallengeService(
        ITwoFactorChallengeRepository challengeRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IJwtTokenService jwtTokenService,
        ICredentialHasher credentialHasher,
        ILogger<TwoFactorChallengeService> logger)
    {
        _challengeRepository = challengeRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _jwtTokenService = jwtTokenService;
        _credentialHasher = credentialHasher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateChallengeAsync(
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

        return token;
    }
}
