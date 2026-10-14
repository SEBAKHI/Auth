namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Builds the addresses a browser is sent to at a relying party's registered
/// URI: authorize's code or error, and end-session's return after signing out.
/// </summary>
public static class RelyingPartyRedirect
{
    /// <summary>
    /// The longest <c>state</c> carried back to a relying party. Authorize
    /// refuses a longer one; end-session ignores the return it came with.
    /// </summary>
    public const int MaxStateLength = 512;

    /// <summary>
    /// Appends one query parameter to a registered address, which may already
    /// carry a query of its own. The value is escaped; the address is used as
    /// registered, never rewritten.
    /// </summary>
    public static string AppendQuery(string url, string name, string value)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{name}={Uri.EscapeDataString(value)}";
    }
}
