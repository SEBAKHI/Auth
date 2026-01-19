using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth_Lib.Configuration;
using Microsoft.Extensions.Options;

namespace Auth_API.Common.Middleware;

/// <summary>
/// Middleware that validates requests come through the API Gateway.
/// Rejects direct API access when gateway validation is enabled.
/// </summary>
public class GatewayTokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayTokenValidationMiddleware> _logger;

    public GatewayTokenValidationMiddleware(
        RequestDelegate next,
        ILogger<GatewayTokenValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<GatewaySettings> settings)
    {
        var gatewaySettings = settings.Value;

        // Skip validation if disabled
        if (!gatewaySettings.ValidationEnabled)
        {
            await _next(context);
            return;
        }

        // Check if path is exempt
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsExemptPath(path, gatewaySettings.ExemptPaths))
        {
            await _next(context);
            return;
        }

        // Validate gateway token
        if (!context.Request.Headers.TryGetValue(gatewaySettings.TokenHeaderName, out var tokenHeader))
        {
            _logger.LogWarning(
                "Request rejected: Missing gateway token header. Path: {Path}, IP: {IP}",
                path,
                context.Connection.RemoteIpAddress);

            await WriteUnauthorizedResponse(context, "Direct API access is not allowed. Please use the API Gateway.");
            return;
        }

        var token = tokenHeader.ToString();

        // Use constant-time comparison to prevent timing attacks
        var expectedToken = gatewaySettings.ExpectedToken ?? string.Empty;
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var actualBytes = Encoding.UTF8.GetBytes(token ?? string.Empty);

        var isValidToken = expectedBytes.Length == actualBytes.Length &&
                          CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);

        if (string.IsNullOrEmpty(token) || !isValidToken)
        {
            _logger.LogWarning(
                "Request rejected: Invalid gateway token. Path: {Path}, IP: {IP}",
                path,
                context.Connection.RemoteIpAddress);

            await WriteUnauthorizedResponse(context, "Invalid gateway token.");
            return;
        }

        await _next(context);
    }

    private static bool IsExemptPath(string path, string[] exemptPaths)
    {
        foreach (var exempt in exemptPaths)
        {
            if (exempt.EndsWith('/'))
            {
                // Prefix match for paths ending with /
                if (path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // Exact match or prefix match
                if (path.Equals(exempt, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(exempt + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var response = new
        {
            type = "https://httpstatuses.com/403",
            title = "Forbidden",
            status = 403,
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
