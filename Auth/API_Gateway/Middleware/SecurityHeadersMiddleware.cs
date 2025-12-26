namespace API_Gateway.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers before the response is sent
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent clickjacking
            headers.XFrameOptions = "DENY";

            // Prevent MIME type sniffing
            headers.XContentTypeOptions = "nosniff";

            // Enable XSS protection
            headers["X-XSS-Protection"] = "1; mode=block";

            // Referrer policy
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content Security Policy (basic)
            headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";

            // Permissions Policy
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            // Remove server header
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
