using System.Net;
using System.Text.Json;

namespace API_Gateway.Middleware;

/// <summary>
/// Global exception handling middleware for the API Gateway.
/// </summary>
public class GatewayExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GatewayExceptionMiddleware(
        RequestDelegate next,
        ILogger<GatewayExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Upstream service error: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadGateway,
                "Bad Gateway", "The upstream service is unavailable or returned an error.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Upstream service timeout");
            await WriteErrorResponse(context, HttpStatusCode.GatewayTimeout,
                "Gateway Timeout", "The upstream service did not respond in time.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in gateway: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError,
                "Internal Server Error",
                _environment.IsDevelopment() ? ex.Message : "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        HttpStatusCode statusCode,
        string title,
        string detail)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var response = new
        {
            type = $"https://httpstatuses.com/{(int)statusCode}",
            title,
            status = (int)statusCode,
            detail,
            instance = context.Request.Path.Value
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
