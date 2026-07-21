using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Auth.Shared.Configuration;

/// <summary>
/// Registers the machine-local configuration layer, <c>appsettings.{Environment}.local.json</c>.
/// Shared by the Auth API and the API Gateway so both resolve local overrides identically.
/// </summary>
/// <remarks>
/// This layer exists to keep secrets out of tracked files. <c>appsettings.{Environment}.json</c> is
/// committed and holds only non-secret defaults, so a fresh clone is configured correctly the moment
/// it is cloned; the <c>.local.json</c> file beside it is git-ignored and is where generated key
/// material and any machine-specific override belong. Splitting the two is what lets the Development
/// storage mode be stated explicitly in a committed file instead of depending on a runtime fallback.
/// </remarks>
public static class LocalConfigurationExtensions
{
    /// <summary>
    /// Builds the local settings file name for an environment.
    /// </summary>
    public static string LocalFileName(string environmentName) =>
        $"appsettings.{environmentName}.local.json";

    /// <summary>
    /// Adds <c>appsettings.{environmentName}.local.json</c> directly after
    /// <c>appsettings.{environmentName}.json</c>, so it overrides the committed environment file
    /// while environment variables and command-line arguments still override it in turn.
    /// </summary>
    /// <param name="builder">The configuration builder to add the source to.</param>
    /// <param name="environmentName">The current environment name (e.g. <c>Development</c>).</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Position matters. Appending would place the file after the environment-variable and
    /// command-line providers that the host registers last, letting a stale local file silently beat
    /// a deliberate <c>SecretManagement__StorageMode</c> env var on a server. The source is therefore
    /// inserted at a computed index rather than appended; if the environment file is not present in
    /// the source list (a host that composes configuration differently), it is appended, which is the
    /// safe degradation because the layer is optional either way.
    /// </remarks>
    public static IConfigurationBuilder AddEnvironmentLocalJsonFile(
        this IConfigurationBuilder builder,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var environmentFile = $"appsettings.{environmentName}.json";
        var localFile = LocalFileName(environmentName);

        var insertAt = -1;
        for (var i = 0; i < builder.Sources.Count; i++)
        {
            if (builder.Sources[i] is JsonConfigurationSource json
                && string.Equals(json.Path, environmentFile, StringComparison.OrdinalIgnoreCase))
            {
                insertAt = i + 1;
            }
        }

        var source = new JsonConfigurationSource
        {
            Path = localFile,
            Optional = true,
            ReloadOnChange = true
        };
        source.ResolveFileProvider();

        if (insertAt >= 0)
        {
            builder.Sources.Insert(insertAt, source);
        }
        else
        {
            builder.Sources.Add(source);
        }

        return builder;
    }
}
