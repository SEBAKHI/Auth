using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// The published privacy-policy document for one (version, language) pair: the
/// exact bytes served to the public, rendered once at publish time.
///
/// The read path returns this and nothing else — no template, no settings, no
/// interpolation. That is what makes an unresolved placeholder unreachable by a
/// reader rather than merely unlikely, and what keeps the notice readable when
/// everything upstream of the stored bytes is unavailable.
/// </summary>
public class PrivacyPolicyArtifact : EntityBase
{
    /// <summary>Gets the owning <see cref="PrivacyPolicyVersion"/>.</summary>
    public Guid VersionId { get; private set; }

    /// <summary>Gets the language this document is served as.</summary>
    public string LanguageCode { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the language the body was written in. When it differs from
    /// <see cref="LanguageCode"/> the document itself says so, in the reader's
    /// language — a silent locale fallback is a deceptive pattern, a disclosed
    /// one is a disclosed limitation.
    /// </summary>
    public string SourceLanguageCode { get; private set; } = string.Empty;

    /// <summary>Gets the complete standalone HTML document.</summary>
    public string Html { get; private set; } = string.Empty;

    /// <summary>Gets the lowercase hex SHA-256 of <see cref="Html"/>.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the disclosure values frozen into <see cref="Html"/>, so the console
    /// can tell an operator the published document no longer describes the
    /// running system. Recorded, never re-applied: silently rewriting a
    /// published legal document is the failure this design exists to prevent.
    /// </summary>
    public string DisclosureJson { get; private set; } = string.Empty;

    public DateTime RenderedAt { get; private set; }

    /// <summary>True when the body is the neutral document standing in.</summary>
    public bool IsLanguageFallback =>
        !string.Equals(LanguageCode, SourceLanguageCode, StringComparison.OrdinalIgnoreCase);

    private PrivacyPolicyArtifact() : base()
    {
    }

    public PrivacyPolicyArtifact(
        Guid id,
        Guid versionId,
        string languageCode,
        string sourceLanguageCode,
        string html,
        string contentHash,
        string disclosureJson,
        DateTime renderedAt) : base(id)
    {
        VersionId = versionId;
        LanguageCode = languageCode;
        SourceLanguageCode = sourceLanguageCode;
        Html = html;
        ContentHash = contentHash;
        DisclosureJson = disclosureJson;
        RenderedAt = renderedAt;
    }

    /// <summary>Creates the document that will be served for one language.</summary>
    public static PrivacyPolicyArtifact Create(
        Guid versionId,
        string languageCode,
        string sourceLanguageCode,
        string html,
        string contentHash,
        string disclosureJson)
    {
        return new PrivacyPolicyArtifact
        {
            VersionId = versionId,
            LanguageCode = languageCode,
            SourceLanguageCode = sourceLanguageCode,
            Html = html,
            ContentHash = contentHash,
            DisclosureJson = disclosureJson,
            RenderedAt = DateTime.UtcNow
        };
    }
}
