namespace Auth.Application.Interfaces;

/// <summary>What a client assertion must satisfy for one request.</summary>
/// <param name="ClientId">The application code of the key's application: <c>iss</c> and <c>sub</c> must both be it.</param>
/// <param name="Audience">The issuer identifier, the sole <c>aud</c> value allowed (draft-ietf-oauth-rfc7523bis).</param>
/// <param name="ClockSkew">The configured clock skew.</param>
public sealed record ClientAssertionRules(string ClientId, string Audience, TimeSpan ClockSkew);

/// <summary>The verdict on one client assertion.</summary>
/// <param name="IsValid">True when every rule held.</param>
/// <param name="FailureReason">For the log only, never for the response: which rule failed.</param>
/// <param name="Jti">The assertion's identifier, when valid.</param>
/// <param name="ExpiresAtUtc">The assertion's <c>exp</c>, when valid.</param>
public sealed record ClientAssertionVerification(bool IsValid, string? FailureReason, string? Jti, DateTime? ExpiresAtUtc)
{
    public static ClientAssertionVerification Refused(string reason) => new(false, reason, null, null);

    public static ClientAssertionVerification Accepted(string jti, DateTime expiresAtUtc) => new(true, null, jti, expiresAtUtc);
}

/// <summary>
/// Reads and verifies the client assertion of the <c>client_credentials</c> grant
/// (<c>private_key_jwt</c>, RFC 7523 §3 and draft-ietf-oauth-rfc7523bis), OI-128.
/// </summary>
public interface IClientAssertionVerifier
{
    /// <summary>
    /// The <c>kid</c> of the assertion's header, read WITHOUT verifying anything, so the key that
    /// verifies the rest can be found. Null when the text is not a compact JWS with a usable kid.
    /// </summary>
    string? ReadKeyId(string assertion);

    /// <summary>
    /// Verifies <paramref name="assertion"/> with the public key <paramref name="publicKeyJwk"/>:
    /// <c>alg</c> exactly ES256; <c>typ</c> absent, <c>JWT</c> or <c>client-authentication+jwt</c>;
    /// <c>iss</c> and <c>sub</c> equal to the client id; <c>aud</c> exactly the issuer identifier
    /// and nothing else; <c>exp</c> present, not past and at most five minutes ahead (plus the
    /// skew); <c>nbf</c> and <c>iat</c>, when present, not in the future beyond the skew; and a
    /// <c>jti</c> of at most 200 characters.
    /// </summary>
    Task<ClientAssertionVerification> VerifyAsync(string assertion, string publicKeyJwk, ClientAssertionRules rules);
}
