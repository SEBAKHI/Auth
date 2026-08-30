using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Auth.Shared.Configuration;

/// <summary>
/// Extension methods that configure how the ASP.NET Core Data Protection key ring
/// is protected at rest, based on the selected <see cref="SecretStorageMode"/>.
/// Shared by the Auth API and the API Gateway so both apps protect the key ring identically.
/// </summary>
public static class AuthDataProtectionExtensions
{
    /// <summary>
    /// Parses a storage-mode string (case-insensitive) into a <see cref="SecretStorageMode"/>.
    /// Null or blank means "not configured" and yields <see cref="SecretStorageMode.PlainText"/>,
    /// matching the class default on <c>SecretManagementSettings</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A non-blank value that names no known mode. This THROWS rather than falling back, because
    /// falling back meant a single typo picked the weakest mode in silence: the configured value
    /// and the effective value were then equal, so the "falling back" warning in Program.cs never
    /// fired, and with the shipped AutoGenerateKeys default the JWT signing key and the refresh
    /// HMAC key were generated and written as PLAIN TEXT into an appsettings file inside the
    /// deploy tree. Refusing to start is the only outcome that cannot be missed.
    /// </exception>
    public static SecretStorageMode ParseStorageMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SecretStorageMode.PlainText;
        }

        var trimmed = value.Trim();

        // Matched against the NAMES, not through Enum.TryParse. TryParse also accepts
        // the underlying ordinals, so a stray "0" in a configuration file would resolve
        // to whichever member happens to sit first — PlainText — which is the same
        // silent downgrade this method exists to stop, arriving by a different door.
        // The configuration contract here is a name; a number is not one.
        foreach (var name in Enum.GetNames<SecretStorageMode>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<SecretStorageMode>(name);
            }
        }

        throw new InvalidOperationException(
            $"Refusing to start: SecretManagement:StorageMode is '{value}', which is not a known storage mode. " +
            $"Valid values (case-insensitive): {string.Join(", ", Enum.GetNames<SecretStorageMode>())}. " +
            "Leave the key blank only if you genuinely intend PlainText; an unrecognised value used to " +
            "resolve to PlainText silently, which strips encryption at rest from the signing key.");
    }

    /// <summary>
    /// Resolves the storage mode actually used for this run, applying the Development fallback.
    /// </summary>
    /// <param name="configuredValue">The configured <c>SecretManagement:StorageMode</c> value.</param>
    /// <param name="isDevelopment">Whether the app is running in the Development environment.</param>
    /// <param name="certificateSettings">The bound <c>DataProtection:Certificate</c> settings.</param>
    /// <returns>The effective mode, which may differ from the configured one only in Development.</returns>
    /// <remarks>
    /// The shipped default is <see cref="SecretStorageMode.Certificate"/>, which needs a certificate
    /// that only a real deployment has provisioned. Applying that verbatim to a fresh clone aborts
    /// startup before anything runs, so Development falls back to <see cref="SecretStorageMode.PlainText"/>
    /// when — and only when — no certificate source is configured. A developer who does configure one
    /// keeps certificate mode and can exercise the production path locally.
    /// <para>
    /// The fallback is deliberately gated on Development rather than on "no certificate": outside
    /// Development a missing certificate must still fail fast, because silently downgrading to
    /// plaintext there would strip encryption at rest from the signing key.
    /// </para>
    /// </remarks>
    public static SecretStorageMode ResolveStorageMode(
        string? configuredValue,
        bool isDevelopment,
        DataProtectionCertificateSettings? certificateSettings)
    {
        var mode = ParseStorageMode(configuredValue);

        if (isDevelopment
            && mode == SecretStorageMode.Certificate
            && certificateSettings?.HasSource() != true)
        {
            return SecretStorageMode.PlainText;
        }

        return mode;
    }

    /// <summary>
    /// Resolves the directory that holds the ASP.NET Core Data Protection key ring, shared by the
    /// Auth API and the API Gateway so both apps read and write the SAME ring.
    /// </summary>
    /// <param name="configuredPath">
    /// The configured <c>DataProtection:KeyPath</c> value. When non-empty it is used verbatim.
    /// </param>
    /// <returns>The configured path, or a safe machine-wide default when none is configured.</returns>
    /// <remarks>
    /// When no path is configured the default is <c>%ProgramData%\AuthSystem\Keys</c>
    /// (<see cref="Environment.SpecialFolder.CommonApplicationData"/>). This is deliberately NOT
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/>: under a Windows Service
    /// (<c>LocalSystem</c>) or an IIS application pool with no loaded user profile,
    /// <c>%LOCALAPPDATA%</c> resolves to <c>C:\Windows\System32\config\systemprofile\AppData\Local</c>,
    /// a directory the process cannot create — producing
    /// "An error occurred while reading the key ring / Access to the path ... is denied."
    /// <para>
    /// The default is also machine-wide rather than per-user, so the two apps share one ring even when
    /// they run under different identities. On locked-down shared hosting (e.g. IIS/Plesk) the app pool
    /// may lack write access to <c>%ProgramData%</c>; set <c>DataProtection:KeyPath</c> explicitly to a
    /// writable folder OUTSIDE the public web root in that case.
    /// </para>
    /// </remarks>
    public static string ResolveKeyRingPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AuthSystem",
            "Keys");
    }

    /// <summary>
    /// Applies key-ring protection to the Data Protection builder according to the storage mode.
    /// <list type="bullet">
    /// <item><see cref="SecretStorageMode.Certificate"/> encrypts the key ring with an X.509 certificate.</item>
    /// <item><see cref="SecretStorageMode.Dpapi"/> relies on the default Windows DPAPI protection.</item>
    /// <item><see cref="SecretStorageMode.PlainText"/> applies no extra protection (secrets live in appsettings).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The Data Protection builder.</param>
    /// <param name="mode">The selected storage mode.</param>
    /// <param name="certificateSettings">Certificate settings, required only for <see cref="SecretStorageMode.Certificate"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="SecretStorageMode.Certificate"/> is selected but the certificate cannot be loaded.
    /// </exception>
    public static IDataProtectionBuilder ConfigureKeyProtection(
        this IDataProtectionBuilder builder,
        SecretStorageMode mode,
        DataProtectionCertificateSettings? certificateSettings)
    {
        if (mode != SecretStorageMode.Certificate)
        {
            // Dpapi: PersistKeysToFileSystem already DPAPI-encrypts the key ring on Windows.
            // PlainText: the key ring is not used to store the app secrets.
            return builder;
        }

        if (certificateSettings is null || !certificateSettings.HasSource())
        {
            throw new InvalidOperationException(
                "SecretManagement:StorageMode is 'Certificate' but no certificate is configured. " +
                "Set 'DataProtection:Certificate:PfxPath' (with a password) or 'DataProtection:Certificate:Thumbprint'.");
        }

        var primary = LoadCertificate(certificateSettings);
        builder.ProtectKeysWithCertificate(primary);

        // Keep previous certificates available so the key ring can still be decrypted after a rotation.
        if (certificateSettings.AdditionalPfxPaths.Count > 0)
        {
            var decryptionCerts = new List<X509Certificate2> { primary };
            var password = certificateSettings.ResolvePassword();

            foreach (var path in certificateSettings.AdditionalPfxPaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    decryptionCerts.Add(LoadPfx(path, password));
                }
            }

            builder.UnprotectKeysWithAnyCertificate(decryptionCerts.ToArray());
        }

        return builder;
    }

    /// <summary>
    /// Loads the Data Protection certificate from a <c>.pfx</c> file or the Windows certificate store.
    /// </summary>
    public static X509Certificate2 LoadCertificate(DataProtectionCertificateSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PfxPath))
        {
            return LoadPfx(settings.PfxPath, settings.ResolvePassword());
        }

        if (!string.IsNullOrWhiteSpace(settings.Thumbprint))
        {
            return LoadFromStore(settings.Thumbprint);
        }

        throw new InvalidOperationException(
            "No Data Protection certificate source configured. Set 'PfxPath' or 'Thumbprint'.");
    }

    private static X509Certificate2 LoadPfx(string pfxPath, string? password)
    {
        if (!File.Exists(pfxPath))
        {
            throw new InvalidOperationException(
                $"Data Protection certificate file not found at '{pfxPath}'.");
        }

        try
        {
            // Keep the private key process-local. Importing into the default Windows key
            // store fails in service/container accounts without a writable user profile,
            // even when the PFX itself is valid and readable.
            return X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                password,
                X509KeyStorageFlags.EphemeralKeySet,
                loaderLimits: null);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load the Data Protection certificate from '{pfxPath}'. " +
                "Verify the file is a valid .pfx and the password is correct.", ex);
        }
    }

    private static X509Certificate2 LoadFromStore(string thumbprint)
    {
        var normalized = thumbprint.Replace(" ", string.Empty).ToUpperInvariant();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
            if (found.Count > 0)
            {
                return found[0];
            }
        }

        throw new InvalidOperationException(
            $"Data Protection certificate with thumbprint '{thumbprint}' was not found in the CurrentUser or LocalMachine store.");
    }
}
