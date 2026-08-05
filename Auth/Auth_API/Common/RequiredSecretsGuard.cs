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
/// signup, in production only, weeks after the merge that introduced it, with
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

        throw new InvalidOperationException(
            $"Refusing to start: {missing.Count} required secret(s) are missing from the resolved " +
            $"configuration under storage mode '{storageMode}'.{Environment.NewLine}{details}{Environment.NewLine}" +
            "Enable SecretManagement:AutoGenerateKeys so the storage mode's generator provisions them, or " +
            "provision them manually in the secrets file. Starting without them would serve traffic that " +
            "fails at the first request touching the secret, not at boot.");
    }
}
