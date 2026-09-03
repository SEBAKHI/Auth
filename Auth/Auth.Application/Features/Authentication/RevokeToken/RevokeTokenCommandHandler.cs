using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RevokeToken;

/// <summary>
/// Handler for the revoke token command.
/// </summary>
public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, ErrorOr<Success>>
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILogger<RevokeTokenCommandHandler> _logger;

    public RevokeTokenCommandHandler(
        IJwtTokenService jwtTokenService,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILogger<RevokeTokenCommandHandler> logger)
    {
        _jwtTokenService = jwtTokenService;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
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
            // Nothing is stored for a token this process did not sign. A
            // fallback used to live here that read the UNVERIFIED JWT anyway
            // and pinned its jti in memory for 24 hours — which made this
            // anonymous endpoint a memory sink: any caller could mint an
            // unsigned token carrying a 250 KB jti (the parser's size limit)
            // and park it here at the edge's twenty requests a minute per
            // address, for a day, with nothing to evict it but the clock. A
            // token that fails validation is forged (nothing of ours to
            // revoke), expired (already dead) or malformed — in every case the
            // correct amount of state to keep is zero.
            //
            // RFC 7009 2.2: "the authorization server responds with HTTP status
            // code 200 if the token has been revoked successfully OR IF THE CLIENT
            // SUBMITTED AN INVALID TOKEN." Answering an error would tell an
            // anonymous caller whether a token was real — turning the revocation
            // endpoint into an oracle for guessing valid tokens. Nothing was
            // revoked because there was nothing to revoke, which is the outcome
            // the caller asked for.
            _logger.LogInformation(
                "Revocation requested for a token that could not be validated; nothing to revoke");
            return Result.Success;
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
        // Compute hash and lookup by hash
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(token);
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
