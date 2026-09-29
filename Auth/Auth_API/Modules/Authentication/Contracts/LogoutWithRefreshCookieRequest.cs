namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for the cookie sign-out.
/// </summary>
public record LogoutWithRefreshCookieRequest
{
    /// <summary>
    /// The session the browser means to end (the access token's <c>sid</c> when it
    /// was signed out). When given and the cookie now belongs to another session,
    /// nothing is ended.
    /// </summary>
    public Guid? SessionId { get; init; }
}
