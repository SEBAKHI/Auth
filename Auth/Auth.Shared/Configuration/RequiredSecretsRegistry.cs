namespace Auth.Shared.Configuration;

/// <summary>
/// How a secret behaves once it exists.
/// </summary>
public enum SecretPermanence
{
    /// <summary>
    /// Rotating invalidates artifacts issued under the old value (tokens,
    /// sessions), but the system recovers on its own once clients re-authenticate.
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
/// making every signup return 500 in production while every test stayed green.
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
            "signs every access token; without it no token can be issued or validated"),

        new RequiredSecret(
            "Jwt:RefreshTokenHmacKeyPlain",
            nameof(SecretConfiguration.RefreshTokenHmacKey),
            SecretPermanence.Rotatable,
            "hashes refresh tokens at rest; without it no session can be refreshed"),

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
