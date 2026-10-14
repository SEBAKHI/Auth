using System.Text.Json.Serialization;

namespace Auth.Application.DTOs;

/// <summary>
/// Response DTO for login operations. When two-factor verification is pending,
/// <see cref="Token"/> and <see cref="User"/> are null and
/// <see cref="TwoFactorChallengeToken"/> carries the challenge to complete.
/// </summary>
public record LoginResponse : ISessionIssuingResponse
{
    TokenResponse? ISessionIssuingResponse.IssuedTokens => Token;

    string? ISessionIssuingResponse.IssuedIdpSessionToken => IdpSessionToken;

    Guid? ISessionIssuingResponse.IssuedIdpSessionId => IdpSessionId;

    bool? ISessionIssuingResponse.IssuedSessionIsPersistent => IdpSessionIsPersistent;

    Guid? ISessionIssuingResponse.IssuedToUserId => User?.Id;

    ISessionIssuingResponse ISessionIssuingResponse.WithRefreshToken(string refreshToken) =>
        Token is null ? this : this with { Token = Token with { RefreshToken = refreshToken } };

    /// <summary>
    /// Gets the token information (null while two-factor verification is pending).
    /// </summary>
    public TokenResponse? Token { get; init; }

    /// <summary>
    /// Gets the authenticated user information (null while two-factor verification is pending).
    /// </summary>
    public UserInfo? User { get; init; }

    /// <summary>
    /// Gets whether the user must change their password.
    /// </summary>
    public bool RequiresPasswordChange { get; init; }

    /// <summary>
    /// Gets whether two-factor verification is required to complete the login.
    /// </summary>
    public bool RequiresTwoFactor { get; init; }

    /// <summary>
    /// Gets the opaque challenge token to present to the two-factor verify
    /// endpoint (null unless <see cref="RequiresTwoFactor"/> is true).
    /// </summary>
    public string? TwoFactorChallengeToken { get; init; }

    /// <summary>
    /// Gets the plain IdP session token minted for this login. NEVER serialized:
    /// the controller moves it into the HttpOnly IdP session cookie and the
    /// browser is the only place it lives outside the hashed database row.
    /// </summary>
    [JsonIgnore]
    public string? IdpSessionToken { get; init; }

    /// <summary>
    /// Gets whether the IdP session minted for this login is persistent: the
    /// person ticked "Stay signed in" (OI-154). The HTTP layer then writes the IdP
    /// session cookie and the app's refresh cookie with an expiry; otherwise both
    /// end with the browser. [JsonIgnore] like the token beside it.
    /// </summary>
    [JsonIgnore]
    public bool IdpSessionIsPersistent { get; init; }

    /// <summary>
    /// Gets the id of the IdP session minted for this login, if any: the session a
    /// same-person sign-in in this browser hands the previous session's refresh
    /// chains to (OI-154). [JsonIgnore] like the token above.
    /// </summary>
    [JsonIgnore]
    public Guid? IdpSessionId { get; init; }

    /// <summary>
    /// Gets the session this sign-in created, for callers that must be able to
    /// revoke exactly this one later.
    /// </summary>
    /// <remarks>
    /// [JsonIgnore] like the SSO token above. It is not a secret - it already
    /// travels inside the access token as "sid" - but nothing in the response
    /// body needs it, and a field on the wire is a field someone starts relying
    /// on. The token exchange stamps it onto the authorization code so a replay
    /// of that code can revoke what the code produced.
    /// </remarks>
    [JsonIgnore]
    public Guid? SessionId { get; init; }
}
