using System.Net;
using System.Text.Json;
using Auth_Localization.Resources.Middleware;
using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Auth_API.Common.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Converts exceptions to standardized Problem Details responses with localized messages.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var localizer = context.RequestServices?.GetService<IStringLocalizer<MiddlewareMessages>>();

        var (statusCode, title, detail, errors) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                Localize(localizer, "Middleware.ValidationError.Title", "Validation Error"),
                Localize(localizer, "Middleware.ValidationError.Detail", "One or more validation errors occurred."),
                validationEx.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            ),
            // NOTE: UnauthorizedAccessException is deliberately NOT mapped to 401. In .NET it is
            // thrown by filesystem/OS ACL denials, not HTTP authentication (which is handled by the
            // JWT middleware before controllers run). Mapping it to 401 disguised a storage-permission
            // failure as an auth failure; it now falls through to 500 and is logged with its stack.
            KeyNotFoundException => (
                HttpStatusCode.NotFound,
                Localize(localizer, "Middleware.NotFound.Title", "Not Found"),
                Localize(localizer, "Middleware.NotFound.Detail", "The requested resource was not found."),
                (object?)null
            ),
            InvalidOperationException invalidOpEx => (
                HttpStatusCode.BadRequest,
                Localize(localizer, "Middleware.InvalidOperation.Title", "Invalid Operation"),
                invalidOpEx.Message,
                (object?)null
            ),
            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                Localize(localizer, "Middleware.InvalidArgument.Title", "Invalid Argument"),
                argEx.Message,
                (object?)null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                Localize(localizer, "Middleware.InternalError.Title", "Internal Server Error"),
                _environment.IsDevelopment()
                    ? exception.Message
                    : Localize(localizer, "Middleware.InternalError.Detail", "An unexpected error occurred."),
                (object?)null
            )
        };

        // Log the exception
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception: {ExceptionType} - {Message}",
                exception.GetType().Name, exception.Message);
        }

        // Create Problem Details response
        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.com/{(int)statusCode}"
        };

        if (errors != null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        if (_environment.IsDevelopment() && statusCode == HttpStatusCode.InternalServerError)
        {
            problemDetails.Extensions["exception"] = new
            {
                type = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace
            };
        }

        // Add correlation ID if available
        if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            problemDetails.Extensions["correlationId"] = correlationId.ToString();
        }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private static string Localize(IStringLocalizer<MiddlewareMessages>? localizer, string key, string fallback)
    {
        if (localizer is null) return fallback;

        var localized = localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}
