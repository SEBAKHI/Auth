namespace Auth_API.Common;

/// <summary>
/// Replaces credential-bearing route values with their parameter name before a
/// request path reaches the log.
/// </summary>
/// <remarks>
/// Some routes carry a secret in the path itself. The invitation endpoints are
/// the live example: <c>/api/v1/Invitations/{token}</c> and its register and
/// accept siblings take a 43-character bearer token as a path segment, and
/// <c>UseSerilogRequestLogging</c> writes <c>RequestPath</c> verbatim into
/// <c>Logs/auth-api-*.log</c>, which production keeps for ninety files. Anyone
/// who could read one line of that file held a working invitation to the
/// organization it named, for as long as the invitation lived.
/// <para>
/// Hashing the token at rest fixed the database half of that exposure and does
/// nothing for this half: the value in the URL is the plaintext, and plaintext
/// is what redeems the invitation.
/// </para>
/// <para>
/// The redaction is deliberately narrow. Substituting every route value would
/// strip the user and organization ids that make an access log worth keeping, so
/// only names on <see cref="SensitiveRouteValueNames"/> are touched, and only
/// when routing actually bound one. Every other path is returned unchanged.
/// </para>
/// <para>
/// The right long-term answer is for these endpoints to take the token in the
/// request body, the way password reset already does. That is an API contract
/// change that has to ship atomically with the SPA, so it is a separate step;
/// this closes the exposure in the meantime and stays correct afterwards.
/// </para>
/// </remarks>
public static class SensitiveRoutePathRedactor
{
    /// <summary>
    /// Route parameter names whose bound value is a credential.
    /// </summary>
    private static readonly string[] SensitiveRouteValueNames = ["token"];

    /// <summary>
    /// Returns <paramref name="requestPath"/> with any credential-bearing route
    /// value replaced by its parameter name in braces.
    /// </summary>
    public static string Redact(HttpContext context, string requestPath)
    {
        if (string.IsNullOrEmpty(requestPath))
        {
            return requestPath;
        }

        var routeValues = context.Request.RouteValues;
        if (routeValues.Count == 0)
        {
            // No endpoint matched - a 404, or a path handled before routing.
            // Nothing was bound, so nothing can be identified as a secret.
            return requestPath;
        }

        var redacted = requestPath;

        foreach (var name in SensitiveRouteValueNames)
        {
            if (routeValues.TryGetValue(name, out var raw)
                && raw is string value
                && value.Length > 0)
            {
                redacted = redacted.Replace(value, $"{{{name}}}", StringComparison.Ordinal);
            }
        }

        return redacted;
    }
}
