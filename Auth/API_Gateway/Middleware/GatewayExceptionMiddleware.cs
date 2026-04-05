using System.Net;
using System.Text.Json;
using Auth_Localization.Resources.Middleware;
using Microsoft.Extensions.Localization;

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
            var localizer = context.RequestServices.GetService<IStringLocalizer<MiddlewareMessages>>();
            await WriteErrorResponse(context, HttpStatusCode.BadGateway,
                Localize(localizer, "Middleware.BadGateway.Title", "Bad Gateway"),
                Localize(localizer, "Middleware.BadGateway.Detail", "The upstream service is unavailable or returned an error."));
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Upstream service timeout");
            var localizer = context.RequestServices.GetService<IStringLocalizer<MiddlewareMessages>>();
            await WriteErrorResponse(context, HttpStatusCode.GatewayTimeout,
                Localize(localizer, "Middleware.GatewayTimeout.Title", "Gateway Timeout"),
                Localize(localizer, "Middleware.GatewayTimeout.Detail", "The upstream service did not respond in time."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in gateway: {Message}", ex.Message);
            var localizer = context.RequestServices.GetService<IStringLocalizer<MiddlewareMessages>>();
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError,
                Localize(localizer, "Middleware.InternalError.Title", "Internal Server Error"),
                _environment.IsDevelopment()
                    ? ex.Message
                    : Localize(localizer, "Middleware.InternalError.Detail", "An unexpected error occurred."));
        }
    }

    private static string Localize(IStringLocalizer<MiddlewareMessages>? localizer, string key, string fallback)
    {
        if (localizer is null) return fallback;
        var localized = localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
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
