using Auth_API.Modules.Authentication.Contracts;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the revoke token command.
/// </summary>
public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, ErrorOr<Success>>
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RevokeTokenCommandHandler> _logger;

    public RevokeTokenCommandHandler(
        IJwtTokenService jwtTokenService,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        ILogger<RevokeTokenCommandHandler> logger)
    {
        _jwtTokenService = jwtTokenService;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return AuthErrors.InvalidToken;
        }

        var tokenType = request.TokenTypeHint;

        // Try to determine token type if not hinted
        if (tokenType == null)
        {
            // JWT tokens contain dots
            tokenType = request.Token.Contains('.')
                ? TokenTypeHint.AccessToken
                : TokenTypeHint.RefreshToken;
        }

        if (tokenType == TokenTypeHint.AccessToken)
        {
            return await RevokeAccessTokenAsync(request.Token, cancellationToken);
        }
        else
        {
            return await RevokeRefreshTokenAsync(request.Token, request.RevokedBy, cancellationToken);
        }
    }

    private async Task<ErrorOr<Success>> RevokeAccessTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // Validate and extract claims from the token
        var validationResult = _jwtTokenService.ValidateAccessToken(token);

        if (validationResult.IsError)
        {
            // Even if validation fails, try to extract the JTI and blacklist it
            var jti = _jwtTokenService.GetTokenId(token);
            if (!string.IsNullOrEmpty(jti))
            {
                // Blacklist with a generous expiration
                _tokenBlacklistService.BlacklistToken(jti, DateTime.UtcNow.AddHours(24));
                _logger.LogInformation("Blacklisted invalid/expired access token with JTI: {Jti}", jti);
                return Result.Success;
            }

            _logger.LogWarning("Failed to revoke access token - could not extract JTI");
            return AuthErrors.InvalidToken;
        }

        var claims = validationResult.Value;
        var tokenId = claims.FindFirst("jti")?.Value;
        var expClaim = claims.FindFirst("exp")?.Value;

        if (string.IsNullOrEmpty(tokenId))
        {
            return AuthErrors.InvalidToken;
        }

        // Calculate expiration time
        var expiresAt = DateTime.UtcNow.AddHours(1); // Default
        if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
        }

        // Add to blacklist
        _tokenBlacklistService.BlacklistToken(tokenId, expiresAt);
        _logger.LogInformation("Revoked access token with JTI: {Jti}", tokenId);

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> RevokeRefreshTokenAsync(
        string token,
        Guid? revokedBy,
        CancellationToken cancellationToken)
    {
        // Hash the token using Argon2id to find it in the database
        var tokenHash = _passwordHasher.HashPassword(token);

        var refreshToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (refreshToken == null)
        {
            _logger.LogWarning("Attempted to revoke non-existent refresh token");
            // Don't reveal whether token exists - return success per RFC 7009
            return Result.Success;
        }

        if (refreshToken.IsRevoked)
        {
            // Already revoked
            return Result.Success;
        }

        refreshToken.Revoke(revokedBy, "Token revocation requested");
        await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);

        _logger.LogInformation(
            "Revoked refresh token for user {UserId}",
            refreshToken.UserId);

        return Result.Success;
    }
}
