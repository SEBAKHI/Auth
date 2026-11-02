using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Shared.Configuration;
using Microsoft.Data.SqlClient;

namespace Auth_API.Common;

/// <summary>
/// Refuses to generate a key while the database still holds data written under the key it
/// would replace (X03). Runs BEFORE anything is written, on every path that generates key
/// material at startup: the plain-text initializer, the first start of the secrets file, the
/// identifier-key top-up and the password pepper.
///
/// <para>
/// A missing secret looks the same on a brand-new install and on a deployment whose secrets
/// file was lost in a move or a restore. Generating is right for the first and silently
/// destructive for the second: everyone is signed out, webhook keys and pending invitations stop
/// matching, peppered passwords stop verifying, and the account-deletion reservations are
/// orphaned for good. The database tells the two apart: a new install has none of the rows
/// below (its seeded System user and administrator are not among them), a deployment that has
/// been used does. The test is never "the database holds users".
/// </para>
///
/// <para>
/// Fail-closed outside Development: a placeholder connection string, an unreachable database
/// or a missing table refuses, because the claim cannot be verified. Development generates
/// with a Warning, so a fresh clone still starts. The way past a refusal on purpose is
/// <see cref="OverrideVariable"/> set to today's UTC date: honoured on that date only, so it
/// cannot stay armed.
/// </para>
/// </summary>
public static class KeyRegenerationGuard
{
    /// <summary>
    /// The environment variable that accepts the losses for one day: its value must be
    /// today's UTC date, <c>yyyy-MM-dd</c>.
    /// </summary>
    public const string OverrideVariable = "AUTH_ALLOW_KEY_REGENERATION";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>One kind of row written under a key, and the query that counts the live ones.</summary>
    /// <param name="Description">What the rows are, as the operator reads it.</param>
    /// <param name="Table">The table, named in a read failure.</param>
    /// <param name="CountSql">Counts the rows a new key would orphan.</param>
    /// <param name="DependsOn">The keys these rows were written under.</param>
    public sealed record KeyDependent(
        string Description,
        string Table,
        string CountSql,
        IReadOnlySet<GeneratedSecret> DependsOn);

    /// <summary>What a new key of each kind costs, and how the operator reads its name.</summary>
    private sealed record SecretFacts(string Label, string Consequence);

    /// <summary>
    /// Every row kind a generated key can orphan. A credential purpose of the HMAC keyring whose
    /// rows outlive a day has a row here (refresh tokens, SSO sessions, webhook keys,
    /// invitations); the shorter ones (codes, links, challenges) expire before a restore would
    /// help. The signing key also covers the applications holding machine keys, whose servers
    /// validate tokens against the JWKS. API keys are Argon2id, so they depend on the pepper,
    /// not on the HMAC key.
    /// </summary>
    public static readonly IReadOnlyList<KeyDependent> Dependents =
    [
        new("live refresh token(s)", "RefreshTokens",
            "SELECT COUNT_BIG(*) FROM [dbo].[RefreshTokens] WHERE [RevokedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
            new HashSet<GeneratedSecret> { GeneratedSecret.CredentialHmacKey, GeneratedSecret.SigningKey }),
        new("live SSO session(s)", "IdpSessions",
            "SELECT COUNT_BIG(*) FROM [dbo].[IdpSessions] WHERE [RevokedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
            new HashSet<GeneratedSecret> { GeneratedSecret.CredentialHmacKey, GeneratedSecret.SigningKey }),
        new("webhook key(s) in use", "WebhookKeys",
            "SELECT COUNT_BIG(*) FROM [dbo].[WebhookKeys] WHERE [RevokedAt] IS NULL AND ([ExpiresAt] IS NULL OR [ExpiresAt] > SYSUTCDATETIME())",
            new HashSet<GeneratedSecret> { GeneratedSecret.CredentialHmacKey }),
        new("pending organization invitation(s)", "OrganizationInvitations",
            "SELECT COUNT_BIG(*) FROM [dbo].[OrganizationInvitations] WHERE [Status] = N'Pending' AND [ExpiresAt] > SYSUTCDATETIME()",
            new HashSet<GeneratedSecret> { GeneratedSecret.CredentialHmacKey }),
        new("active machine key(s)", "ApplicationMachineKeys",
            "SELECT COUNT_BIG(*) FROM [dbo].[ApplicationMachineKeys] WHERE [RevokedAt] IS NULL",
            new HashSet<GeneratedSecret> { GeneratedSecret.SigningKey }),
        new("account-deletion reservation(s)", "AccountDeletionTombstones",
            "SELECT COUNT_BIG(*) FROM [dbo].[AccountDeletionTombstones]",
            new HashSet<GeneratedSecret> { GeneratedSecret.IdentifierKey }),
        new("peppered password hash(es)", "Users",
            "SELECT COUNT_BIG(*) FROM [dbo].[Users] WHERE [PasswordHash] LIKE N'%,keyid=%'",
            new HashSet<GeneratedSecret> { GeneratedSecret.PasswordPepper }),
        new("peppered API key(s) in use", "ApiKeys",
            "SELECT COUNT_BIG(*) FROM [dbo].[ApiKeys] WHERE [RevokedAt] IS NULL AND ([ExpiresAt] IS NULL OR [ExpiresAt] > SYSUTCDATETIME()) AND [KeyHash] LIKE N'%,keyid=%'",
            new HashSet<GeneratedSecret> { GeneratedSecret.PasswordPepper }),
        new("account(s) with peppered recovery codes", "TwoFactorAuth",
            "SELECT COUNT_BIG(*) FROM [dbo].[TwoFactorAuth] WHERE [RecoveryCodes] LIKE N'%,keyid=%'",
            new HashSet<GeneratedSecret> { GeneratedSecret.PasswordPepper }),
    ];

    private static readonly FrozenDictionary<GeneratedSecret, SecretFacts> Facts =
        new Dictionary<GeneratedSecret, SecretFacts>
        {
            [GeneratedSecret.SigningKey] = new(
                "JWT signing key",
                "every issued access token stops validating, and applications and machine clients that cache " +
                "the signing key (JWKS) reject new tokens until they fetch it again"),
            [GeneratedSecret.CredentialHmacKey] = new(
                "credential HMAC key",
                "every stored refresh token, SSO session, webhook key and invitation stops matching: everyone is " +
                "signed out, every webhook key must be issued again and pending invitations no longer open"),
            [GeneratedSecret.GatewayToken] = new(
                "gateway token",
                "the API Gateway is refused until it is given the new token"),
            [GeneratedSecret.IdentifierKey] = new(
                "account-deletion identifier key",
                "every account-deletion reservation is orphaned for good: deleted addresses become registrable " +
                "again, against the published policy, and no later step can undo it"),
            [GeneratedSecret.PasswordPepper] = new(
                "password pepper",
                "every peppered password, API key and recovery code stops verifying: those users cannot sign in " +
                "with their password until they reset it"),
        }.ToFrozenDictionary();

    /// <summary>
    /// Short, non-reversible identifier for a key, safe to log. Lets an operator
    /// tell "the same key as last boot" from "a new one" without ever writing
    /// key material to a log file.
    /// </summary>
    public static string Fingerprint(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return "none";
        }

        var digest = SHA256.HashData(Convert.FromBase64String(base64Key));
        return Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// <summary>What a secrets-file generation pass is about to write.</summary>
    public static IReadOnlyCollection<GeneratedSecret> GeneratedBy(KeyGenerationResult result) =>
        new (bool Generated, GeneratedSecret Secret)[]
            {
                (result.RsaKeyGenerated, GeneratedSecret.SigningKey),
                (result.HmacKeyGenerated, GeneratedSecret.CredentialHmacKey),
                (result.GatewayTokenGenerated, GeneratedSecret.GatewayToken),
                (result.IdentifierKeyGenerated, GeneratedSecret.IdentifierKey)
            }
            .Where(candidate => candidate.Generated)
            .Select(candidate => candidate.Secret)
            .ToList();

    /// <summary>
    /// Throws when generating <paramref name="secrets"/> would orphan live data, outside
    /// Development and without today's override. Returns the Warning to log when generation goes
    /// ahead over data that depends on the old keys (Development, or the override), and null
    /// when nothing depends on them.
    /// </summary>
    public static string? EnsureSafeToGenerate(
        IReadOnlyCollection<GeneratedSecret> secrets,
        string? connectionString,
        bool isDevelopment,
        string? overrideValue,
        DateOnly todayUtc) =>
        EnsureSafeToGenerate(
            secrets,
            dependents => CountRows(connectionString, dependents),
            isDevelopment,
            overrideValue,
            todayUtc);

    /// <inheritdoc cref="EnsureSafeToGenerate(IReadOnlyCollection{GeneratedSecret}, string?, bool, string?, DateOnly)"/>
    /// <param name="countRows">Counts each dependent's rows, in order; throws when it cannot.</param>
    public static string? EnsureSafeToGenerate(
        IReadOnlyCollection<GeneratedSecret> secrets,
        Func<IReadOnlyList<KeyDependent>, IReadOnlyList<long>> countRows,
        bool isDevelopment,
        string? overrideValue,
        DateOnly todayUtc)
    {
        var relevant = Dependents.Where(dependent => dependent.DependsOn.Overlaps(secrets)).ToList();
        if (relevant.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", secrets.Select(secret => "the " + Facts[secret].Label));
        var today = todayUtc.ToString(DateFormat, CultureInfo.InvariantCulture);
        var overridden = string.Equals(overrideValue?.Trim(), today, StringComparison.Ordinal);
        var acceptedBy = isDevelopment
            ? "Development"
            : $"{OverrideVariable}={today}";

        IReadOnlyList<long> counts;
        try
        {
            counts = countRows(relevant);
        }
        catch (Exception ex)
        {
            if (isDevelopment || overridden)
            {
                return $"{acceptedBy}: generating {names} without checking the database for data written under " +
                       $"the keys they replace: {ex.Message}.";
            }

            throw new InvalidOperationException(
                $"Refusing to generate {names}: whether the database still holds data written under the keys they " +
                $"would replace could not be checked: {ex.Message}.{Environment.NewLine}" +
                "A deployment that has run before must restore its secrets instead of generating new ones; a " +
                "brand-new install must reach its database first." + Environment.NewLine +
                OverrideHint(overrideValue, today),
                ex);
        }

        var found = relevant
            .Zip(counts, (dependent, count) => (dependent, count))
            .Where(row => row.count > 0)
            .ToList();
        if (found.Count == 0)
        {
            return null;
        }

        var rows = string.Join(
            Environment.NewLine,
            found.Select(row => $"  - {row.count} {row.dependent.Description}"));
        var consequences = string.Join(
            Environment.NewLine,
            secrets
                .Where(secret => found.Any(row => row.dependent.DependsOn.Contains(secret)))
                .Select(secret => $"  - a new {Facts[secret].Label}: {Facts[secret].Consequence}"));

        if (isDevelopment || overridden)
        {
            return $"{acceptedBy}: generating {names} although the database holds data written under the keys " +
                   $"they replace:{Environment.NewLine}{rows}{Environment.NewLine}{consequences}";
        }

        throw new InvalidOperationException(
            $"Refusing to generate {names}: the database already holds data written under the key(s) a new one " +
            $"would replace:{Environment.NewLine}{rows}{Environment.NewLine}" +
            $"Generating now means:{Environment.NewLine}{consequences}{Environment.NewLine}" +
            "This deployment has run before, so its secrets are missing or were not read: the secrets file " +
            "(SecretManagement:SecretFilePath) in Certificate/Dpapi mode, or the appsettings file that holds them " +
            "in PlainText mode. Restore it from backup, with the Data Protection key ring and certificate, and " +
            "restart." + Environment.NewLine +
            OverrideHint(overrideValue, today));
    }

    private static string OverrideHint(string? overrideValue, string today)
    {
        var stale = string.IsNullOrWhiteSpace(overrideValue)
            ? string.Empty
            : $"{OverrideVariable} is set, but not to today's UTC date ({today}), so it was ignored. ";

        return stale +
               $"Only if no backup exists and these losses are accepted: set the environment variable " +
               $"{OverrideVariable}={today} for one start (web.config <environmentVariables> on IIS), then remove " +
               "it. It is honoured on that UTC date only.";
    }

    /// <summary>Counts each dependent's rows on one connection, naming what failed.</summary>
    private static IReadOnlyList<long> CountRows(string? connectionString, IReadOnlyList<KeyDependent> dependents)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("no AuthDb connection string is configured");
        }

        if (ConnectionStringGuard.IsPlaceholder(connectionString))
        {
            throw new InvalidOperationException(
                $"the AuthDb connection string is still the placeholder '{ConnectionStringGuard.Placeholder}' " +
                "(a deployment that keeps it in the secrets file is missing that file)");
        }

        using var connection = Open(connectionString);

        var counts = new List<long>(dependents.Count);
        foreach (var dependent in dependents)
        {
            using var command = connection.CreateCommand();
            command.CommandText = dependent.CountSql;
            command.CommandTimeout = 30;

            try
            {
                counts.Add(Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"[dbo].[{dependent.Table}] could not be read ({ex.Message})", ex);
            }
        }

        return counts;
    }

    private static SqlConnection Open(string connectionString)
    {
        SqlConnection? connection = null;
        try
        {
            connection = new SqlConnection(connectionString);
            connection.Open();
            return connection;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            connection?.Dispose();
            throw new InvalidOperationException($"the database could not be reached ({ex.Message})", ex);
        }
    }
}
