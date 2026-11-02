using Auth.Shared.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Common;

/// <summary>
/// Refuses to start when any secret declared in <see cref="RequiredSecretsRegistry"/>
/// is missing from the resolved configuration.
///
/// <para>
/// MUST run AFTER the secret layer has been added and after any generation
/// pass, so it inspects the values the process will actually use.
/// </para>
///
/// <para>
/// This exists because the alternative is worse in exactly the way that is
/// hardest to notice. Secret consumers resolve their key material lazily —
/// <c>IdentifierHasher</c> holds a <c>Lazy&lt;byte[]&gt;</c> whose comment
/// claimed a missing key "only fails deletion operations, never unrelated
/// requests". That was false: the reservation guard sits on all four
/// account-creation paths, so the missing key surfaced as a 500 on every
/// signup, only where the secret is missing, long after the merge that introduced it, with
/// a full green test suite. Lazy resolution does not reduce the blast radius
/// of a missing secret; it only delays the report until the worst possible
/// observer — an end user — triggers it. Fail at boot instead.
/// </para>
/// </summary>
public static class RequiredSecretsGuard
{
    public static void EnsureAllPresent(IConfiguration configuration, string storageMode)
    {
        var missing = RequiredSecretsRegistry.FindMissing(key => configuration[key]);

        if (missing.Count == 0)
        {
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            missing.Select(secret =>
                $"  - {secret.ConfigurationKey} ({secret.Permanence}) — {secret.Purpose}. " +
                $"In Certificate/Dpapi mode it is carried by SecretConfiguration.{secret.SecretFileProperty}."));

        // Two situations end here, and the way out of one destroys the other (X03). This check runs
        // before the database is reachable, so the operator is the one who tells them apart: the
        // advice to generate is given for a brand-new install only.
        throw new InvalidOperationException(
            $"Refusing to start: {missing.Count} required secret(s) are missing from the resolved " +
            $"configuration under storage mode '{storageMode}'.{Environment.NewLine}{details}{Environment.NewLine}" +
            "Which case is this?" + Environment.NewLine +
            "  - This deployment has run before: its secrets file is missing or was not read (check " +
            "SecretManagement:SecretFilePath and any decryption error logged above). Restore it from backup, with " +
            "the Data Protection key ring and certificate, and restart. Do not enable " +
            "SecretManagement:AutoGenerateKeys: new keys over a database in use sign everyone out, make every " +
            "webhook key and pending invitation invalid, and permanently break the account-deletion reservations." +
            Environment.NewLine +
            "  - A brand-new install only: set SecretManagement:AutoGenerateKeys to true for one run, so the storage " +
            "mode's generator provisions them, then set it back to false." + Environment.NewLine +
            "Starting without them would serve traffic that fails at the first request touching the secret, not at boot.");
    }
}
