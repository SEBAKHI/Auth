using Auth.Shared.Http.ErrorContract;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Auth_API.Common.Errors;

/// <summary>
/// The one way to refuse a bearer token after it was read: by the blacklist, before any action
/// runs, and by userinfo, when the token's subject can no longer be served. Both answers are the
/// same, so a caller cannot tell a revoked token from a deleted or locked account.
/// </summary>
public static class BearerTokenRejection
{
    /// <summary>
    /// A 401 whose reason becomes the problem's code (ADR 0001). No body: the status-code pages
    /// write it. WWW-Authenticate says the bearer token itself was refused (RFC 6750).
    /// </summary>
    public static void Reject(HttpContext context, string reason)
    {
        context.Items[ProblemItems.Code] = reason;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
    }
}

/// <summary>
/// For an action that calls <see cref="BearerTokenRejection.Reject"/>: keeps its 401 identical to
/// the blacklist's. The blacklist answers before MVC runs; inside MVC, API versioning adds its
/// report headers when the response starts, and those alone would tell the two 401s apart.
/// </summary>
/// <remarks>
/// A resource filter, so its callback is registered before the versioning filter's and, the
/// response's start callbacks running last-registered first, runs after it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SameAnswerAsBlacklistAttribute : Attribute, IResourceFilter
{
    private static readonly string[] VersionReportHeaders = ["api-supported-versions", "api-deprecated-versions"];

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var response = context.HttpContext.Response;
        response.OnStarting(() =>
        {
            if (response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                foreach (var header in VersionReportHeaders)
                {
                    response.Headers.Remove(header);
                }
            }

            return Task.CompletedTask;
        });
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
