using System.Text;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The one-time code Google sent back to an app's callback page, and the PKCE verifier the
/// app kept for that sign-in. The callback address itself is not part of the request: the
/// API builds it from the verified Origin.
/// </summary>
public record GoogleCodeExchangeRequest
{
    /// <summary>Gets the code from the callback address (single-use, minutes-long).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the PKCE verifier (RFC 7636: 43 to 128 unreserved characters).</summary>
    public string CodeVerifier { get; init; } = string.Empty;

    /// <summary>
    /// Names the members without their values: the framework writes an action's arguments
    /// into its Trace log, and a code with its verifier is a sign-in for a few minutes.
    /// </summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = [redacted], CodeVerifier = [redacted]");
        return true;
    }
}

/// <summary>
/// The ID token Google minted, unvalidated. The app presents it to <c>external-login</c>,
/// which validates its signature, audience and server-issued nonce.
/// </summary>
/// <param name="IdToken">The ID token, and nothing else from Google's answer.</param>
public record GoogleCodeExchangeResponse(string IdToken)
{
    /// <summary>Names the member without its value, so no log line can carry the token.</summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("IdToken = [redacted]");
        return true;
    }
}
