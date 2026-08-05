using Auth.Domain.Entities;

namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// Keeps the currently published documents in memory.
///
/// The public policy path is anonymous and uncacheable downstream (an
/// intermediary must not be allowed to serve a superseded legal notice), so
/// without this every reader would cost a database round-trip on an endpoint
/// nobody has to authenticate to reach.
/// </summary>
public interface IPolicyArtifactCache
{
    /// <summary>Returns the cached document for a language, or null.</summary>
    PrivacyPolicyArtifact? GetPublished(string languageCode);

    /// <summary>Caches one document, after a read had to fall through.</summary>
    void SetPublished(string languageCode, PrivacyPolicyArtifact artifact);

    /// <summary>
    /// Swaps the whole published set in one step, as part of publishing.
    ///
    /// Replace rather than evict, deliberately: eviction would make the first
    /// reader after a publish re-fetch from the database, which turns a database
    /// blip at exactly that moment into a broken legal page. The publisher has
    /// just rendered these documents, so the correct bytes are already in hand.
    /// </summary>
    void ReplacePublished(IReadOnlyList<PrivacyPolicyArtifact> artifacts);
}
