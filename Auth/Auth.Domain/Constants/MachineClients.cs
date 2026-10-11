namespace Auth.Domain.Constants;

/// <summary>
/// Machine clients (OI-128): an application's own server, which proves who it is with a key pair
/// at the token endpoint (the <c>client_credentials</c> grant with <c>private_key_jwt</c>) and is
/// answered with a short machine access token. No user stands behind such a token.
/// </summary>
public static class MachineClients
{
    /// <summary>
    /// The one audience of every machine access token. It is not shaped like an application code
    /// (<c>:</c> is outside the code rule), so the userinfo scheme refuses it by construction, and
    /// it is not <c>Jwt:Audience</c>, so the platform scheme refuses it too.
    /// </summary>
    public const string Audience = "urn:authsystem:machine";

    /// <summary>
    /// The only algorithm a machine key, and so a client assertion, may use: ECDSA on P-256 with
    /// SHA-256.
    /// </summary>
    public const string SigningAlgorithm = "ES256";

    /// <summary>
    /// How many unrevoked machine keys one application may hold at once: enough to roll a key over
    /// by overlap (add the new one, move the server to it, revoke the old one), and no more.
    /// </summary>
    public const int MaxActiveKeysPerApplication = 3;

    /// <summary>
    /// The lifetime of a machine access token. A code constant, not a setting: revoking a machine
    /// key takes effect within this time, because a machine token cannot be revoked by itself.
    /// </summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest a client assertion may live, from now to its <c>exp</c>, before the clock skew.
    /// </summary>
    public static readonly TimeSpan MaxAssertionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The machine scopes: a closed list. A machine token carries some of these and nothing else;
    /// none is a permission code, and no permission check reads them.
    /// </summary>
    public static class Scopes
    {
        public const string ApiKeysValidate = "apikeys.validate";
        public const string WebhookKeysValidate = "webhookkeys.validate";
        public const string TokensIntrospect = "tokens.introspect";

        /// <summary>Every machine scope, in the order a token lists them.</summary>
        public static readonly IReadOnlyList<string> All = [ApiKeysValidate, WebhookKeysValidate, TokensIntrospect];
    }
}
