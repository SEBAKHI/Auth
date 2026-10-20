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
    /// <remarks>
    /// Two groups (OI-202). <see cref="All"/> is granted to every machine key, and a request that
    /// names no scope receives all of it. <see cref="ConsentOnly"/> is granted only when the
    /// request names it AND the application's administrator consented to it; an empty request
    /// never includes it, so a machine client that asks for nothing receives what it always did.
    /// </remarks>
    public static class Scopes
    {
        public const string ApiKeysValidate = "apikeys.validate";
        public const string WebhookKeysValidate = "webhookkeys.validate";
        public const string TokensIntrospect = "tokens.introspect";

        /// <summary>
        /// Reading, with no user, the directory of every organization that enabled the
        /// application: the organization, its members who hold one of this application's roles
        /// (with those roles), and the invitations that carry this application's role (OI-202).
        /// Never an organization that did not enable the application. The application-only
        /// counterpart of the user scope <c>organizations.read</c> (OI-194), which reads as the
        /// signed-in user: the same stem, and <c>.all</c> for every organization of the application.
        /// </summary>
        public const string OrganizationsReadAll = "organizations.read.all";

        /// <summary>
        /// Every machine scope granted by default, in the order a token lists them: what a request
        /// with no <c>scope</c> receives. <see cref="ConsentOnly"/> is not in it.
        /// </summary>
        public static readonly IReadOnlyList<string> All = [ApiKeysValidate, WebhookKeysValidate, TokensIntrospect];

        /// <summary>
        /// The machine scopes granted only when asked for and consented to by the application's
        /// administrator, in the order a token lists them, after <see cref="All"/>.
        /// </summary>
        public static readonly IReadOnlyList<string> ConsentOnly = [OrganizationsReadAll];

        /// <summary>
        /// Whether a token's space-delimited <c>scope</c> claim names <paramref name="scope"/>,
        /// exactly (case-sensitive).
        /// </summary>
        public static bool Grants(string? scopeClaim, string scope) =>
            scopeClaim is not null
            && scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
    }
}
