using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Auth.Sdk.Handlers;

/// <summary>
/// Turns the challenge of a request whose key could not be checked into 503 with
/// <c>Retry-After</c> (SDK 2.0): the AuthSystem was throttling, failing or unreachable, which says
/// nothing about the key, and a 401 would tell an honest caller that its key was revoked.
/// </summary>
/// <remarks>
/// Applied as the response starts rather than in the scheme's own challenge: an endpoint guarded
/// by <c>[Authorize(AuthenticationSchemes = "Bearer,ApiKey")]</c> challenges both schemes, and
/// the Bearer challenge, whichever runs last, would leave a 401. Only a 401 is turned: a request
/// another scheme authenticated keeps its own answer.
/// </remarks>
internal static class UnavailableAnswer
{
    public static void Arrange(HttpContext context, TimeSpan retryAfter)
    {
        var response = context.Response;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        response.OnStarting(() =>
        {
            if (response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                response.Headers[HeaderNames.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);
                response.Headers.Remove(HeaderNames.WWWAuthenticate);
            }

            return Task.CompletedTask;
        });
    }
}
