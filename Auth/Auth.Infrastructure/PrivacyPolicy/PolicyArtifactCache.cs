using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Auth.Infrastructure.PrivacyPolicy;

/// <summary>
/// In-process cache of the published policy documents.
///
/// A document is a few tens of kilobytes and there are seven of them, so the
/// whole published set is small enough to hold without a size policy. Entries
/// carry no expiry: they are replaced when a publish replaces them, and a
/// time-based expiry would only add a moment when a reader pays for a database
/// round-trip on a page that must not depend on one.
/// </summary>
public class PolicyArtifactCache : IPolicyArtifactCache
{
    private const string KeyPrefix = "privacy-policy:published:";

    private readonly IMemoryCache _cache;

    public PolicyArtifactCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public PrivacyPolicyArtifact? GetPublished(string languageCode) =>
        _cache.TryGetValue(Key(languageCode), out PrivacyPolicyArtifact? artifact)
            ? artifact
            : null;

    /// <inheritdoc />
    public void SetPublished(string languageCode, PrivacyPolicyArtifact artifact) =>
        _cache.Set(Key(languageCode), artifact);

    /// <inheritdoc />
    public void ReplacePublished(IReadOnlyList<PrivacyPolicyArtifact> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            _cache.Set(Key(artifact.LanguageCode), artifact);
        }
    }

    private static string Key(string languageCode) =>
        KeyPrefix + languageCode.ToLowerInvariant();
}
