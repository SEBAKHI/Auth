using Auth_Lib.Application.Interfaces;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Enums;
using Auth_Lib.Domain.Constants;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using System.Security.Claims;

namespace Auth_Lib.Application.Features.Authentication.IntrospectToken;

/// <summary>
/// Handler for the introspect token query.
/// </summary>
public class IntrospectTokenQueryHandler : IRequestHandler<IntrospectTokenQuery, ErrorOr<IntrospectTokenResponse>>
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly ILogger<IntrospectTokenQueryHandler> _logger;

    public IntrospectTokenQueryHandler(
        IJwtTokenService jwtTokenService,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        ILogger<IntrospectTokenQueryHandler> logger)
    {
        _jwtTokenService = jwtTokenService;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _logger = logger;
    }

    public async Task<ErrorOr<IntrospectTokenResponse>> Handle(
        IntrospectTokenQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return new IntrospectTokenResponse { Active = false };
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
            return await IntrospectAccessTokenAsync(request.Token, cancellationToken);
        }
        else
        {
            return await IntrospectRefreshTokenAsync(request.Token, cancellationToken);
        }
    }

    private Task<ErrorOr<IntrospectTokenResponse>> IntrospectAccessTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // Validate the token
        var validationResult = _jwtTokenService.ValidateAccessToken(token);

        if (validationResult.IsError)
        {
            _logger.LogDebug("Token introspection failed - invalid token");
            return Task.FromResult<ErrorOr<IntrospectTokenResponse>>(
                new IntrospectTokenResponse { Active = false });
        }

        var claims = validationResult.Value;

        // Check if blacklisted
        var jti = GetClaimValue(claims, JwtClaimNames.JwtId);
        if (!string.IsNullOrEmpty(jti) && _tokenBlacklistService.IsTokenBlacklisted(jti))
        {
            _logger.LogDebug("Token introspection - token is blacklisted: {Jti}", jti);
            return Task.FromResult<ErrorOr<IntrospectTokenResponse>>(
                new IntrospectTokenResponse { Active = false });
        }

        // Check user-level revocation
        var subClaim = GetClaimValue(claims, JwtClaimNames.Subject);
        var iatClaim = GetClaimValue(claims, JwtClaimNames.IssuedAt);
        if (Guid.TryParse(subClaim, out var userId) &&
            long.TryParse(iatClaim, out var iatUnix))
        {
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
            if (_tokenBlacklistService.AreUserTokensBlacklisted(userId, issuedAt))
            {
                _logger.LogDebug("Token introspection - user tokens are revoked for user: {UserId}", userId);
                return Task.FromResult<ErrorOr<IntrospectTokenResponse>>(
                    new IntrospectTokenResponse { Active = false });
            }
        }

        // Build response
        var response = new IntrospectTokenResponse
        {
            Active = true,
            TokenType = "bearer",
            Sub = subClaim,
            Jti = jti,
            Iss = GetClaimValue(claims, JwtClaimNames.Issuer),
            Aud = GetClaimValue(claims, JwtClaimNames.Audience),
            Email = GetClaimValue(claims, JwtClaimNames.Email),
            Username = GetClaimValue(claims, JwtClaimNames.Email),
            Exp = GetLongClaimValue(claims, JwtClaimNames.Expiration),
            Iat = GetLongClaimValue(claims, JwtClaimNames.IssuedAt),
            Nbf = GetLongClaimValue(claims, "nbf"),
            Roles = claims.FindAll(JwtClaimNames.Roles).Select(c => c.Value).ToList(),
            Permissions = claims.FindAll(JwtClaimNames.Permissions).Select(c => c.Value).ToList()
        };

        return Task.FromResult<ErrorOr<IntrospectTokenResponse>>(response);
    }

    private async Task<ErrorOr<IntrospectTokenResponse>> IntrospectRefreshTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // Compute hash and lookup by hash
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(token);
        var refreshToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (refreshToken == null)
        {
            _logger.LogDebug("Token introspection - refresh token not found");
            return new IntrospectTokenResponse { Active = false };
        }

        // Check if active (not revoked and not expired)
        if (refreshToken.IsRevoked || refreshToken.IsExpired())
        {
            _logger.LogDebug("Token introspection - refresh token is not active");
            return new IntrospectTokenResponse { Active = false };
        }

        // Build response for refresh token
        var response = new IntrospectTokenResponse
        {
            Active = true,
            TokenType = "refresh_token",
            Sub = refreshToken.UserId.ToString(),
            Exp = new DateTimeOffset(refreshToken.ExpiresAt).ToUnixTimeSeconds(),
            Iat = new DateTimeOffset(refreshToken.CreatedAt).ToUnixTimeSeconds()
        };

        return response;
    }

    private static string? GetClaimValue(ClaimsPrincipal claims, string claimType)
    {
        return claims.FindFirst(claimType)?.Value;
    }

    private static long? GetLongClaimValue(ClaimsPrincipal claims, string claimType)
    {
        var value = claims.FindFirst(claimType)?.Value;
        if (!string.IsNullOrEmpty(value) && long.TryParse(value, out var result))
        {
            return result;
        }
        return null;
    }
}
