using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// The privacy-policy document for one (version, language) pair, stored as the
/// JSON shape the accounts app renders. Content lives in the database — not in
/// source — so legal text is editable from the console without a deployment,
/// exactly like notification templates.
/// </summary>
public class PrivacyPolicyTranslation : EntityBase
{
    /// <summary>Gets the owning <see cref="PrivacyPolicyVersion"/>.</summary>
    public Guid VersionId { get; private set; }

    /// <summary>Gets the ISO language code (en, ar, tr, fr, zh, ur, fa).</summary>
    public string LanguageCode { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the AUTHORED document as JSON, matching the frontend's
    /// <c>PrivacyPolicyContent</c> contract. Disclosures are written as
    /// <c>{{token}}</c> placeholders; they are resolved when the version is
    /// published, not when the policy is read.
    /// </summary>
    public string ContentJson { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    private PrivacyPolicyTranslation() : base()
    {
    }

    public PrivacyPolicyTranslation(
        Guid id,
        Guid versionId,
        string languageCode,
        string contentJson,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        VersionId = versionId;
        LanguageCode = languageCode;
        ContentJson = contentJson;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>Creates the document for one language of a version.</summary>
    public static PrivacyPolicyTranslation Create(
        Guid versionId, string languageCode, string contentJson, Guid createdBy)
    {
        return new PrivacyPolicyTranslation
        {
            VersionId = versionId,
            LanguageCode = languageCode,
            ContentJson = contentJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Replaces the authored document.
    ///
    /// No artifact is touched: editing is not publishing, and a saved edit must
    /// not change what the public is currently being served.
    /// </summary>
    public void UpdateContent(string contentJson, Guid modifiedBy)
    {
        ContentJson = contentJson;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}
