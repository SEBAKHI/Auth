using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Foundation_Lib.Api.Responses;
using AuthShared_Lib.Constants;

namespace AuthShared_Lib.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
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

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var response = new ErrorResponse
        {
            Message = "An unexpected error occurred. Please try again later.",
            ErrorCode = ErrorCodes.INTERNAL_SERVER_ERROR,
            Timestamp = DateTime.UtcNow
        };

        // In development, include exception details
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            response.Errors = new Dictionary<string, string>
            {
                { "ExceptionMessage", exception.Message },
                { "StackTrace", exception.StackTrace ?? string.Empty }
            };
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(response, options);

        return context.Response.WriteAsync(json);
    }
}
