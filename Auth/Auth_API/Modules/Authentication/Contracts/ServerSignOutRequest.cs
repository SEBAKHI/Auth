using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// A server-side application signs a person out from its server (OI-183), bound from
/// application/x-www-form-urlencoded with the snake_case names of the token endpoint, so the
/// server sends the same fields it sends on its refresh grant.
/// </summary>
public class ServerSignOutRequest
{
    /// <summary>The person's session's refresh token, bound to the application's sign-in key.</summary>
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>Optional; when sent, it must name the application whose key signed the assertion.</summary>
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    /// <summary><c>urn:ietf:params:oauth:client-assertion-type:jwt-bearer</c> (RFC 7523 §2.2).</summary>
    [FromForm(Name = "client_assertion_type")]
    public string? ClientAssertionType { get; set; }

    /// <summary>The assertion signed with the application's sign-in key; its audience is the issuer.</summary>
    [FromForm(Name = "client_assertion")]
    public string? ClientAssertion { get; set; }
}
