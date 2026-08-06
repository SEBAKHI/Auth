using Microsoft.AspNetCore.Http;

namespace Auth.Shared.Http;

/// <summary>
/// Adds the baseline browser-security headers shared by every public HTTP host.
/// </summary>
/// <remarks>
/// A downstream endpoint or proxied service remains authoritative for a
/// specialized Content Security Policy. The middleware supplies the baseline
/// only when no policy exists; overwriting a downstream policy can make a valid
/// response unusable, because the browser enforces the final header rather than
/// the policy at the component that produced the document.
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private const string DefaultContentSecurityPolicy =
        "default-src 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    /// <param name="next">The next component in the HTTP pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Adds missing security headers without replacing endpoint policy.</summary>
    /// <param name="context">The current HTTP request context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.XFrameOptions = "DENY";
            headers.XContentTypeOptions = "nosniff";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (string.IsNullOrEmpty(headers.ContentSecurityPolicy))
            {
                headers.ContentSecurityPolicy = DefaultContentSecurityPolicy;
            }

            headers["Permissions-Policy"] =
                "geolocation=(), microphone=(), camera=()";

            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
