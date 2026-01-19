namespace Auth_API.Common.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses.
/// Implements OWASP recommended security headers.
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

            // Prevent clickjacking - OWASP recommendation
            headers.XFrameOptions = "DENY";

            // Prevent MIME type sniffing - OWASP recommendation
            headers.XContentTypeOptions = "nosniff";

            // Enable XSS protection (legacy, but still useful for older browsers)
            headers["X-XSS-Protection"] = "1; mode=block";

            // Control referrer information leakage
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content Security Policy - prevent XSS and data injection
            headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";

            // Restrict browser features
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            // Remove server identification headers
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
