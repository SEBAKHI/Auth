using System.IdentityModel.Tokens.Jwt;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;

namespace Auth_API.Common.Middleware;

/// <summary>
/// Middleware that validates JWT tokens against the blacklist.
/// Rejects requests with tokens that have been revoked via logout.
/// </summary>
public class JwtBlacklistValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtBlacklistValidationMiddleware> _logger;

    public JwtBlacklistValidationMiddleware(
        RequestDelegate next,
        ILogger<JwtBlacklistValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITokenBlacklistService blacklistService)
    {
        // Only check authenticated requests with Bearer tokens
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);
            return;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                await _next(context);
                return;
            }

            var jwtToken = handler.ReadJwtToken(token);
            var jti = jwtToken.Id;
            var sid = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.Sid)?.Value;
            var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.Subject)?.Value;
            var iatClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.IssuedAt)?.Value;

            // Check if this specific token is blacklisted
            if (!string.IsNullOrEmpty(jti) && blacklistService.IsTokenBlacklisted(jti))
            {
                _logger.LogWarning("Rejected blacklisted token with JTI: {Jti}", jti);
                BearerTokenRejection.Reject(context, ChallengeReasonCodes.TokenRevoked);
                return;
            }

            // Check if the whole login session has been revoked (terminated session)
            if (!string.IsNullOrEmpty(sid) && blacklistService.IsSessionBlacklisted(sid))
            {
                _logger.LogWarning("Rejected token for revoked session: {SessionId}", sid);
                BearerTokenRejection.Reject(context, ChallengeReasonCodes.SessionRevoked);
                return;
            }

            // Check if all user tokens issued before a certain time are blacklisted
            if (!string.IsNullOrEmpty(subClaim) && Guid.TryParse(subClaim, out var userId) &&
                !string.IsNullOrEmpty(iatClaim) && long.TryParse(iatClaim, out var iatUnix))
            {
                var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
                if (blacklistService.AreUserTokensBlacklisted(userId, issuedAt))
                {
                    _logger.LogWarning("Rejected token for user {UserId} issued at {IssuedAt} - all tokens revoked", userId, issuedAt);
                    BearerTokenRejection.Reject(context, ChallengeReasonCodes.TokenRevoked);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // If we can't parse the token, let the normal JWT validation handle it
            _logger.LogDebug(ex, "Error parsing JWT for blacklist check - deferring to normal validation");
        }

        await _next(context);
    }
}
