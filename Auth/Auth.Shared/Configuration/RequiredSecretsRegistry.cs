namespace Auth.Shared.Configuration;

/// <summary>
/// How a secret behaves once it exists.
/// </summary>
public enum SecretPermanence
{
    /// <summary>
    /// Can be replaced without corrupting data. The refresh-token HMAC key sits on
    /// a keyring (S11): after a rotation through the keyring, a credential issued
    /// under the previous key keeps working through its purpose's overlap window,
    /// so there is no mass re-authentication; refresh tokens and sign-in sessions
    /// not re-issued within 30 days of the cutover sign in again. Replacing any
    /// other rotatable value invalidates what was issued under the old one, and
    /// the system recovers once clients re-authenticate.
    /// </summary>
    Rotatable,

    /// <summary>
    /// Rotating silently orphans data already derived under the old value.
    /// Nothing fails loudly, so a regenerated permanent secret is a data
    /// corruption event that surfaces only as wrong answers much later.
    /// </summary>
    Permanent
}

/// <summary>
/// A secret the Auth API cannot serve traffic without.
/// </summary>
/// <param name="ConfigurationKey">Flattened configuration key the process reads (e.g. <c>Jwt:PrivateKeyPem</c>).</param>
/// <param name="SecretFileProperty">
/// Property name on <see cref="SecretConfiguration"/> that carries it in Certificate/Dpapi mode.
/// The generator-parity test matches on this, so it must be the real property name.
/// </param>
/// <param name="Permanence">Whether regenerating it is recoverable.</param>
/// <param name="Purpose">Why the process needs it, for the startup failure message.</param>
public sealed record RequiredSecret(
    string ConfigurationKey,
    string SecretFileProperty,
    SecretPermanence Permanence,
    string Purpose);

/// <summary>
/// The single declaration of every secret the Auth API must have before it can
/// serve a request, in every storage mode.
///
/// <para>
/// This registry exists because the two secret generators drifted apart. A
/// secret added to the PlainText generator only
/// (<see cref="PlainTextSecretInitializer"/>) is present in Development and
/// absent in Production, where the encrypted-file generator
/// (<c>DpapiSecretService.GenerateMissingKeysAsync</c>) never learned about it.
/// The failure then surfaces at the first request that touches the secret —
/// which for the account-deletion identifier key was <c>POST /register</c>,
/// making every signup return 500 under Certificate/Dpapi storage while every test stayed green.
/// </para>
///
/// <para>
/// Two mechanisms hang off this list and together close that class of bug:
/// a generator-parity test asserts both generators cover every entry, and
/// <c>RequiredSecretsGuard</c> refuses to start the process when any entry is
/// missing from the resolved configuration. Adding a secret means adding it
/// here first; the test then tells you which generator is still missing it.
/// </para>
/// </summary>
public static class RequiredSecretsRegistry
{
    /// <summary>
    /// Unconditionally required secrets. Conditional material (the Argon2id
    /// pepper, which is only required while <c>Password:Pepper:Enabled</c> is
    /// on) is provisioned by its own startup block and is deliberately not
    /// listed here: this registry answers "the process cannot run without it",
    /// with no predicate to evaluate.
    /// </summary>
    public static readonly IReadOnlyList<RequiredSecret> All =
    [
        new RequiredSecret(
            "Jwt:PrivateKeyPem",
            nameof(SecretConfiguration.JwtPrivateKeyPem),
            SecretPermanence.Rotatable,
            "signs every access token: the current key of the signing keyring (S12), which rotates through the ring's " +
            "overlap without signing anyone out; without it no token can be issued or validated"),

        new RequiredSecret(
            "Jwt:RefreshTokenHmacKeyPlain",
            nameof(SecretConfiguration.RefreshTokenHmacKey),
            SecretPermanence.Rotatable,
            "the HMAC keyring's current key: fingerprints refresh tokens, SSO sessions, authorization codes " +
            "and request handles, reset and invitation links, two-factor challenges, provider nonces, step-up " +
            "tickets, one-time codes, pending-registration handles and webhook keys; without it no session " +
            "can be refreshed and no link or code can be checked"),

        new RequiredSecret(
            "Gateway:ExpectedToken",
            nameof(SecretConfiguration.GatewayToken),
            SecretPermanence.Rotatable,
            "authenticates the API Gateway; without it every proxied request is rejected"),

        new RequiredSecret(
            "AccountDeletion:IdentifierHmacKeyPlain",
            nameof(SecretConfiguration.AccountDeletionIdentifierHmacKey),
            SecretPermanence.Permanent,
            "hashes identifiers for the deletion registry; the reservation guard runs on all four " +
            "account-creation paths, so without it registration and account deletion both fail")
    ];

    /// <summary>
    /// Secrets the API Gateway process cannot run without.
    ///
    /// <para>
    /// Only the shared gateway token: the gateway stamps it on every proxied
    /// request and the API compares it against its own copy, so the two halves
    /// come from the same <c>GatewayToken</c> secret and drift between them
    /// rejects 100% of traffic. The gateway has always failed fast on a missing
    /// token outside Development; listing it here puts both processes on the
    /// same declaration, so a secret added for one is visibly missing from the
    /// other instead of being discovered in production.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<RequiredSecret> Gateway =
    [
        new RequiredSecret(
            "Gateway:Token",
            nameof(SecretConfiguration.GatewayToken),
            SecretPermanence.Rotatable,
            "stamped on every proxied request; the Auth API rejects anything without it")
    ];

    /// <summary>
    /// Secrets a vendor issues and the deployment's owner transcribes (S13, C10, D-T5 A): optional,
    /// so never in <see cref="FindMissing"/>'s default list and never a reason to refuse a boot,
    /// and never generated, by either generator (<see cref="KeyMaterialGenerator"/> and
    /// <c>PlainTextSecretInitializer</c> produce only the entries of <see cref="All"/>): a value
    /// made up here would look provisioned and fail at the vendor on first use.
    ///
    /// <para>
    /// Mapped from the secrets file like every other entry, owned by the secret layer
    /// (<c>SecretOwnedKeys</c>), refused in plaintext in Production (<c>ProductionSecretGuard</c>),
    /// and named in CI's crown-jewel step. RequiredSecretsCoverageTests holds all of that.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<RequiredSecret> VendorIssued =
    [
        new RequiredSecret(
            "BotDefense:TurnstileSecret",
            nameof(SecretConfiguration.TurnstileSecretKey),
            SecretPermanence.Rotatable,
            "Cloudflare Turnstile's secret key: the bot check asks Cloudflare with it whether a token is good; without it " +
            "the check is not in place (Unconfigured, or Misconfigured while a sitekey is set). Rotated in Cloudflare, " +
            "where the old one stays valid for two hours, then set again from the console")
    ];

    /// <summary>
    /// Returns the entries whose configuration value is missing or blank.
    /// </summary>
    public static IReadOnlyList<RequiredSecret> FindMissing(
        Func<string, string?> readConfiguration,
        IReadOnlyList<RequiredSecret>? secrets = null)
    {
        var missing = new List<RequiredSecret>();

        foreach (var secret in secrets ?? All)
        {
            if (string.IsNullOrWhiteSpace(readConfiguration(secret.ConfigurationKey)))
            {
                missing.Add(secret);
            }
        }

        return missing;
    }
}
