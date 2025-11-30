using Microsoft.AspNetCore.Http;

namespace AuthShared_Lib.Middleware;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;

    public JwtMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var token = context.Request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();

        if (!string.IsNullOrEmpty(token))
        {
            // Token validation is handled by JwtBearer authentication
            // This middleware can be used for additional custom logic
        }

        await _next(context);
    }
}
