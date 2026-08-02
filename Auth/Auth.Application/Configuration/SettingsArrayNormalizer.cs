namespace Auth.Application.Configuration;

/// <summary>
/// Repairs the two ways .NET configuration mis-renders an array that the system
/// settings console has SHRUNK. Both are properties of the platform, not of this
/// codebase, and both silently keep an operator's removed entry alive at runtime:
/// <list type="number">
/// <item><b>Binder append.</b> <c>ConfigurationBinder</c> binds an array by copying
/// the array the property ALREADY holds and then appending every configured index.
/// A non-empty property initializer is therefore not a default at all — it is a
/// permanent prefix that no configuration layer can remove. Settings classes here
/// keep their intended defaults in a static field and initialize the property to
/// an empty array; <see cref="Resolve"/> puts the default back only when
/// configuration genuinely supplied nothing.</item>
/// <item><b>Shrink tombstones.</b> Configuration merges arrays index-wise and a
/// higher layer cannot delete a lower layer's key, so the database settings
/// provider masks removed elements with empty-string tombstones
/// (DbSettingsConfigurationProvider). Those tombstones bind as real <c>""</c>
/// members. Left in place they are matched like any other value — an empty exempt
/// path prefix-matches every request, an empty content type matches an upload that
/// declares none. <see cref="Resolve"/> strips them centrally so no consumer has
/// to remember to.</item>
/// </list>
/// <para>
/// Applied through <c>PostConfigure</c>, so it re-runs on every rebind and keeps
/// holding for <c>IOptionsSnapshot</c> and <c>IOptionsMonitor</c> consumers after a
/// settings change.
/// </para>
/// </summary>
public static class SettingsArrayNormalizer
{
    /// <summary>
    /// Applies the normalization to a bound <see cref="GatewaySettings"/>. Registered
    /// as a <c>PostConfigure</c> action so it re-runs on every rebind; also the single
    /// definition the parity and shrink tests exercise, so the production rule and the
    /// tested rule can never drift.
    /// </summary>
    public static void Apply(GatewaySettings settings)
        => settings.ExemptPaths = Resolve(settings.ExemptPaths, GatewaySettings.DefaultExemptPaths);

    /// <summary>Applies the normalization to a bound <see cref="ImageStorageSettings"/>.</summary>
    public static void Apply(ImageStorageSettings settings)
        => settings.AllowedContentTypes = Resolve(
            settings.AllowedContentTypes, ImageStorageSettings.DefaultAllowedContentTypes);

    /// <summary>
    /// Returns the effective array for a bound configuration array: tombstones and
    /// blank entries removed, and <paramref name="fallback"/> substituted only when
    /// configuration contributed no usable entry at all.
    /// </summary>
    /// <param name="bound">The array as the configuration binder produced it.</param>
    /// <param name="fallback">The value to use when configuration is silent.</param>
    public static string[] Resolve(string[]? bound, string[] fallback)
    {
        if (bound is null || bound.Length == 0)
        {
            return fallback;
        }

        var kept = bound
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .ToArray();

        // An operator who deliberately empties the list gets an empty list only if
        // the list was never populated in the first place; a shrink to nothing is
        // indistinguishable from "unset" at this layer, so the safe default wins.
        return kept.Length == 0 ? fallback : kept;
    }
}
