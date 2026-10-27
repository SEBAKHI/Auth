using System.Text.Json.Serialization;
using Auth.Application.Features.Authentication.SendSignInEmailCode;

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
    /// Gets the factors the pending sign-in may be completed with, by their wire
    /// names (<c>totp</c>, <c>recovery</c>, <c>email</c>), in the order a client offers
    /// them (S24). Null — and then not written, so a client built before the emailed
    /// code reads the body it always read — unless <see cref="RequiresTwoFactor"/> is
    /// true; also null when the account's factor could not be read.
    /// </summary>
    public IReadOnlyList<string>? TwoFactorMethods { get; init; }

    /// <summary>
    /// Gets where the emailed code went, when the server sent one as it opened the
    /// challenge (D-243): the answer the resend gives, so the second step starts with
    /// the code on its way. Null — and then not written — when no code was sent: the
    /// methods do not offer it, or the send was refused or failed, and the client asks
    /// for one. Only after a correct first factor, like the resend's own answer.
    /// </summary>
    public SignInEmailCodeResponse? TwoFactorEmailCode { get; init; }

    /// <summary>
    /// Gets what a sign-in by emailed code does next when it issued no session and no
    /// challenge (OI-215): the password sign-in continues it, the address's account
    /// waits for deletion, or the address has no account. Null — and then not
    /// written — on every other answer, so a client built before the code sign-in
    /// reads the body it always read.
    /// </summary>
    public EmailCodeNextStep? NextStep { get; init; }

    /// <summary>
    /// Gets the plain IdP session token minted for this login. NEVER serialized:
    /// the controller moves it into the HttpOnly IdP session cookie and the
    /// browser is the only place it lives outside the hashed database row.
    /// </summary>
    [JsonIgnore]
    public string? IdpSessionToken { get; init; }

    /// <summary>
    /// Gets whether the IdP session minted for this login is persistent: the
    /// person's "Stay signed in" (OI-154) as <c>IdentityProvider:StaySignedInMode</c>
    /// resolves it (OI-165). The HTTP layer then writes the IdP
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
