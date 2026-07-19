using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// OAuth 2.0 token request (RFC 6749 §4.1.3 / §6), bound from
/// application/x-www-form-urlencoded with the spec's snake_case field names.
/// </summary>
public class OAuthTokenRequest
{
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; set; }

    [FromForm(Name = "code")]
    public string? Code { get; set; }

    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }

    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }

    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
}
