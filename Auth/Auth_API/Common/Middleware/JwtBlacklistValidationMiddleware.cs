using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Constants;

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
            var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.Subject)?.Value;
            var iatClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtClaimNames.IssuedAt)?.Value;

            // Check if this specific token is blacklisted
            if (!string.IsNullOrEmpty(jti) && blacklistService.IsTokenBlacklisted(jti))
            {
                _logger.LogWarning("Rejected blacklisted token with JTI: {Jti}", jti);
                await WriteUnauthorizedResponse(context, "Token has been revoked.");
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
                    await WriteUnauthorizedResponse(context, "Token has been revoked. Please log in again.");
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

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var response = new
        {
            type = "https://httpstatuses.com/401",
            title = "Unauthorized",
            status = 401,
            detail = message,
            instance = context.Request.Path.Value
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
