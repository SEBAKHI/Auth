using Microsoft.AspNetCore.Mvc;
using Auth.Domain.Enums;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request to introspect a token and get its metadata (RFC 7662).
/// </summary>
/// <remarks>
/// Form-encoded, not JSON, and snake_case — because RFC 7662 says so, and the
/// entry this endpoint is advertised under in the discovery document is a promise
/// that it behaves that way. A standard client library sends
/// <c>application/x-www-form-urlencoded</c> and was answered 415 for as long as
/// this bound from a JSON body. Same shape the token endpoint already uses.
/// </remarks>
public class IntrospectTokenRequest
{
    /// <summary>
    /// The token.
    /// </summary>
    [FromForm(Name = "token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional hint about which kind of token this is.
    /// </summary>
    /// <remarks>
    /// Bound as a STRING rather than as <see cref="TokenTypeHint"/>. The enum
    /// carries [JsonStringEnumMemberName("access_token")], but form binding does
    /// not go through the JSON converter — it matches the C# member name — so a
    /// conformant client sending <c>access_token</c> would silently fail to bind
    /// and the hint would be lost. Parsed explicitly below instead.
    /// </remarks>
    [FromForm(Name = "token_type_hint")]
    public string? TokenTypeHint { get; set; }

    /// <summary>
    /// The hint as the domain enum, or null when absent or unrecognised.
    /// </summary>
    /// <remarks>
    /// An unknown value is ignored rather than refused: RFC 7662 makes the
    /// hint optional and tells the server to fall back to searching all token
    /// types, so rejecting the request would be stricter than the standard and
    /// would break a client that means well.
    /// </remarks>
    public TokenTypeHint? ParsedTokenTypeHint => TokenTypeHint?.Trim().ToLowerInvariant() switch
    {
        "access_token" => Auth.Domain.Enums.TokenTypeHint.AccessToken,
        "refresh_token" => Auth.Domain.Enums.TokenTypeHint.RefreshToken,
        _ => null,
    };
}
