using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Auth.Shared.Http;

/// <summary>
/// Marks a token request that carries a client assertion (OI-178), so the edge's rate limiter
/// can count it apart: a server-side application or a machine client sends every one of its
/// people's token requests from one address, with its assertion.
/// </summary>
/// <remarks>
/// <para>
/// Presence only. The edge holds no application key and cannot verify a signature, so anyone
/// can attach an assertion; the ceiling these requests get is therefore per address too, and
/// the API, which verifies, counts each verified one on its application's budget and refuses
/// the rest on their address's. What this keeps at the edge is the rest of the token traffic,
/// which stays on its own per-address budget.
/// </para>
/// <para>
/// Reads only a small form with a declared length: anything else is left unmarked and counted
/// as it was before. The body is buffered and rewound, so the request is forwarded exactly as
/// it arrived.
/// </para>
/// </remarks>
public sealed class ClientAssertionPresenceMiddleware
{
    /// <summary>The largest form read. A token request's form is a few hundred bytes to a few kilobytes.</summary>
    public const int MaxFormBytes = 16 * 1024;

    private const string ItemKey = "Auth.Shared.ClientAssertionPresent";

    private readonly RequestDelegate _next;
    private readonly Func<HttpContext, bool> _appliesTo;

    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="appliesTo">Whether the request is one to look at: the token endpoint's route.</param>
    public ClientAssertionPresenceMiddleware(RequestDelegate next, Func<HttpContext, bool> appliesTo)
    {
        _next = next;
        _appliesTo = appliesTo;
    }

    /// <summary>Whether this request was marked as carrying a client assertion.</summary>
    public static bool IsMarked(HttpContext context) => context.Items.ContainsKey(ItemKey);

    public async Task InvokeAsync(HttpContext context)
    {
        if (_appliesTo(context) && await CarriesAssertionAsync(context.Request, context.RequestAborted))
        {
            context.Items[ItemKey] = true;
        }

        await _next(context);
    }

    private static async Task<bool> CarriesAssertionAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!HttpMethods.IsPost(request.Method)
            || !request.HasFormContentType
            || request.ContentLength is not (> 0 and <= MaxFormBytes))
        {
            return false;
        }

        request.EnableBuffering(bufferThreshold: MaxFormBytes, bufferLimit: MaxFormBytes);
        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            return !StringValues.IsNullOrEmpty(form["client_assertion"]);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
