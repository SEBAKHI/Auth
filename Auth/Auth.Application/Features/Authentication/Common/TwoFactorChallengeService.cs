using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared service that creates login-time two-factor challenges. Used by both
/// LoginCommandHandler and ExternalLoginCommandHandler so the challenge
/// creation logic is not duplicated.
/// </summary>
public class TwoFactorChallengeService : ITwoFactorChallengeService
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILogger<TwoFactorChallengeService> _logger;

    public TwoFactorChallengeService(
        ITwoFactorChallengeRepository challengeRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILogger<TwoFactorChallengeService> logger)
    {
        _challengeRepository = challengeRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateChallengeAsync(User user, string? ipAddress, CancellationToken cancellationToken)
    {
        // High-entropy opaque token; reuses the CSPRNG refresh-token generator.
        var token = _jwtTokenService.GenerateRefreshToken();
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(token);

        // A new login supersedes any pending challenge for the same user.
        await _challengeRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var challenge = TwoFactorChallenge.Create(user.Id, tokenHash, ipAddress);
        await _challengeRepository.CreateAsync(challenge, cancellationToken);

        _logger.LogInformation(
            "Two-factor challenge issued for user {UserId} from {IpAddress}",
            user.Id, ipAddress);

        return token;
    }
}
