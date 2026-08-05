using System.Net;
using System.Text.Json;
using Auth_Localization.Resources.Middleware;
using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The CLIENT hung up mid-request — a monitoring probe with a short
            // timeout, a browser navigating away, a closed tab. Nothing failed
            // on our side, and there is no longer a connection to write a
            // response to.
            //
            // Treated as an error before this, it produced a stack trace at
            // [ERR] plus a "responded 500" line for a request that was simply
            // abandoned. On /health that was actively harmful: the endpoint
            // runs only the trivial "self" check, so a 500 there reads as "the
            // API is dead" to whatever automation polls it, and the noise
            // buries real failures in the same log.
            _logger.LogDebug(
                "Request aborted by the client: {Method} {Path}",
                context.Request.Method, context.Request.Path);
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
            // Raw exception messages are English-only and can leak internals, so
            // clients get a localized generic detail outside Development. The full
            // exception is still logged below.
            InvalidOperationException invalidOpEx => (
                HttpStatusCode.BadRequest,
                Localize(localizer, "Middleware.InvalidOperation.Title", "Invalid Operation"),
                _environment.IsDevelopment()
                    ? invalidOpEx.Message
                    : Localize(localizer, "Middleware.InvalidOperation.Detail", "The request could not be processed."),
                (object?)null
            ),
            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                Localize(localizer, "Middleware.InvalidArgument.Title", "Invalid Argument"),
                _environment.IsDevelopment()
                    ? argEx.Message
                    : Localize(localizer, "Middleware.InvalidArgument.Detail", "One or more arguments were invalid."),
                (object?)null
            ),
            // SQL error 547 = FK constraint violation. Defense-in-depth for the
            // hard-delete paths still in the codebase (Roles, Permissions,
            // NotificationTemplates, ...): a referenced row is a conflict, not an
            // internal error. Application deletion itself no longer hits this
            // (soft delete), but any remaining DELETE gets a 409 instead of a 500.
            SqlException { Number: 547 } => (
                HttpStatusCode.Conflict,
                Localize(localizer, "Middleware.Conflict.Title", "Conflict"),
                Localize(localizer, "Middleware.Conflict.Detail",
                    "The operation could not be completed because related records reference this resource."),
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
