using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Auth.Shared.Configuration;

/// <summary>
/// Result of a plain-text secret initialization pass.
/// </summary>
public sealed class PlainTextSecretResult
{
    /// <summary>Whether any new secret was generated during this pass.</summary>
    public bool Generated { get; init; }

    /// <summary>
    /// Flattened configuration values (e.g. <c>Jwt:PrivateKeyPem</c>) that should be added to the
    /// running configuration so the current process uses them immediately. Empty when nothing changed.
    /// </summary>
    public Dictionary<string, string?> ConfigValues { get; init; } = new();

    /// <summary>Names of the secrets that were generated during this pass.</summary>
    public List<string> GeneratedKeys { get; init; } = new();

    /// <summary>The RSA public key PEM, for logging when a new key pair is generated.</summary>
    public string? PublicKeyPem { get; init; }

    /// <summary>A non-null message when the generated secrets could not be persisted to disk.</summary>
    public string? PersistError { get; init; }
}

/// <summary>
/// Stores the application's secrets as plain text inside an appsettings JSON file
/// (<see cref="SecretStorageMode.PlainText"/>). On first run it generates any missing
/// secret, persists it to the target file for durability, and returns the values so the
/// current process can use them without waiting for a configuration reload.
/// </summary>
public static class PlainTextSecretInitializer
{
    /// <summary>
    /// Resolves the appsettings file that generated plain-text secrets are written to.
    /// </summary>
    /// <param name="configuredFile">The configured <c>SecretManagement:PlainTextTargetFile</c>, if any.</param>
    /// <param name="environmentName">The current environment name (e.g. <c>Development</c>).</param>
    /// <returns>The configured file when set, otherwise the environment's git-ignored local file.</returns>
    /// <remarks>
    /// Defaults to <c>appsettings.{EnvironmentName}.local.json</c>: the environment's own layer, so
    /// the next run reads the keys back instead of regenerating them, and the git-ignored one, so
    /// generated key material never lands in the committed <c>appsettings.{EnvironmentName}.json</c>.
    /// <para>
    /// The original fixed default was <c>appsettings.Production.json</c> for every environment, which
    /// broke twice in Development: that file is not part of the Development configuration, so each run
    /// generated fresh keys — invalidating every token issued before the restart — and it quietly
    /// seeded the Production config with plaintext dev keys, the exact state
    /// <c>ProductionSecretGuard</c> refuses to boot on.
    /// </para>
    /// </remarks>
    public static string ResolveTargetFile(string? configuredFile, string environmentName)
    {
        return !string.IsNullOrWhiteSpace(configuredFile)
            ? configuredFile
            : LocalConfigurationExtensions.LocalFileName(environmentName);
    }

    /// <summary>
    /// Ensures the RSA signing key, HMAC key, and gateway token exist in configuration.
    /// Generates any missing values (when <paramref name="autoGenerate"/> is true) and writes
    /// them to <paramref name="targetFile"/>.
    /// </summary>
    /// <param name="configuration">The current configuration, used to detect already-present secrets.</param>
    /// <param name="contentRootPath">Application content root, used to resolve a relative <paramref name="targetFile"/>.</param>
    /// <param name="autoGenerate">Whether missing secrets should be generated.</param>
    /// <param name="targetFile">Appsettings file to persist generated secrets to (absolute or relative to content root).</param>
    public static PlainTextSecretResult EnsureSecrets(
        IConfiguration configuration,
        string contentRootPath,
        bool autoGenerate,
        string targetFile)
    {
        var existingPrivateKey = configuration["Jwt:PrivateKeyPem"];
        var existingHmac = configuration["Jwt:RefreshTokenHmacKeyPlain"];
        var existingGateway = configuration["Gateway:ExpectedToken"] ?? configuration["Gateway:Token"];
        var existingIdentifierHmac = configuration["AccountDeletion:IdentifierHmacKeyPlain"];

        var needsPrivateKey = string.IsNullOrWhiteSpace(existingPrivateKey);
        var needsHmac = string.IsNullOrWhiteSpace(existingHmac);
        var needsGateway = string.IsNullOrWhiteSpace(existingGateway);
        var needsIdentifierHmac = string.IsNullOrWhiteSpace(existingIdentifierHmac);

        // Everything already present, or generation disabled: nothing to do.
        if ((!needsPrivateKey && !needsHmac && !needsGateway && !needsIdentifierHmac) || !autoGenerate)
        {
            return new PlainTextSecretResult { Generated = false };
        }

        var configValues = new Dictionary<string, string?>();
        var generatedKeys = new List<string>();
        string? publicKeyPem = null;

        if (needsPrivateKey)
        {
            var (privateKeyPem, generatedPublicKeyPem) = KeyMaterialGenerator.GenerateRsaKeyPair();
            publicKeyPem = generatedPublicKeyPem;
            configValues["Jwt:PrivateKeyPem"] = privateKeyPem;
            configValues["Jwt:PublicKeyPem"] = generatedPublicKeyPem;
            generatedKeys.Add("Jwt:PrivateKeyPem");
        }

        if (needsHmac)
        {
            configValues["Jwt:RefreshTokenHmacKeyPlain"] = KeyMaterialGenerator.GenerateHmacKeyBase64();
            generatedKeys.Add("Jwt:RefreshTokenHmacKeyPlain");
        }

        if (needsGateway)
        {
            var token = KeyMaterialGenerator.GenerateGatewayToken();
            configValues["Gateway:ExpectedToken"] = token;
            configValues["Gateway:Token"] = token;
            generatedKeys.Add("Gateway:ExpectedToken");
        }

        if (needsIdentifierHmac)
        {
            // Account-deletion identifier hashing (tombstone reservations).
            // PERMANENT once generated: reservations depend on stable hashes.
            configValues["AccountDeletion:IdentifierHmacKeyPlain"] = KeyMaterialGenerator.GenerateHmacKeyBase64();
            generatedKeys.Add("AccountDeletion:IdentifierHmacKeyPlain");
        }

        var persistError = PersistToFile(contentRootPath, targetFile, configValues);

        return new PlainTextSecretResult
        {
            Generated = true,
            ConfigValues = configValues,
            GeneratedKeys = generatedKeys,
            PublicKeyPem = publicKeyPem,
            PersistError = persistError
        };
    }

    /// <summary>
    /// Persists the given flattened configuration values into the target appsettings file,
    /// preserving existing content. Returns an error message on failure, or <c>null</c> on success.
    /// Used to provision an individual secret (e.g. the password pepper) on demand.
    /// </summary>
    public static string? Persist(
        string contentRootPath,
        string targetFile,
        Dictionary<string, string?> values)
        => PersistToFile(contentRootPath, targetFile, values);

    /// <summary>
    /// Writes the generated values into the target appsettings file, preserving existing content.
    /// Returns an error message on failure, or <c>null</c> on success.
    /// </summary>
    private static string? PersistToFile(
        string contentRootPath,
        string targetFile,
        Dictionary<string, string?> configValues)
    {
        var path = Path.IsPathRooted(targetFile)
            ? targetFile
            : Path.Combine(contentRootPath, targetFile);

        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                root = JsonNode.Parse(
                    text,
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            foreach (var (flatKey, value) in configValues)
            {
                SetNested(root, flatKey, value);
            }

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            // Write to a temp file, then RENAME it over the target. The previous
            // version of this said "write atomically" and then called
            // File.Copy(overwrite: true), which truncates the destination and
            // copies into it — the same exposed window the temp file was there to
            // avoid, only now with the secrets already generated and this file the
            // only place they exist. A rename within one volume either happened or
            // did not.
            //
            // The temp name carries a guid rather than a fixed .tmp suffix: two
            // processes starting together would otherwise write the same path,
            // and one would publish a file the other was still writing.
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                        // The write already failed or already succeeded; a leftover
                        // temp file must not replace the reason we are here.
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Could not persist generated secrets to '{path}': {ex.Message}";
        }
    }

    /// <summary>
    /// Sets a colon-delimited configuration key (e.g. <c>Jwt:PrivateKeyPem</c>) inside the JSON object,
    /// creating intermediate objects as needed.
    /// </summary>
    private static void SetNested(JsonObject root, string flatKey, string? value)
    {
        var segments = flatKey.Split(':');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }
}
