using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// A single-language rendering source (subject + bodies) belonging to one
/// template version. Translations never version independently: they live and
/// die with their owning <see cref="NotificationTemplateVersion"/>.
/// </summary>
public class NotificationTemplateTranslation : EntityBase
{
    /// <summary>
    /// Gets the ID of the owning template version.
    /// </summary>
    public Guid VersionId { get; private set; }

    /// <summary>
    /// Gets the language code ('en', 'ar', 'tr', 'fr', 'zh', 'ur', 'fa').
    /// </summary>
    public string LanguageCode { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the subject line (a Liquid template).
    /// </summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the HTML body (a Liquid template) rendered into the layout's content slot.
    /// </summary>
    public string BodyHtml { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional plain-text body (a Liquid template). When null, the
    /// plain-text alternative is derived from <see cref="BodyHtml"/> at render time.
    /// </summary>
    public string? BodyText { get; private set; }

    public DateTime? ModifiedAt { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    private NotificationTemplateTranslation() : base()
    {
    }

    public NotificationTemplateTranslation(
        Guid id,
        Guid versionId,
        string languageCode,
        string subject,
        string bodyHtml,
        string? bodyText,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        VersionId = versionId;
        LanguageCode = languageCode;
        Subject = subject;
        BodyHtml = bodyHtml;
        BodyText = bodyText;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    internal static NotificationTemplateTranslation Create(
        Guid versionId,
        string languageCode,
        string subject,
        string bodyHtml,
        string? bodyText)
    {
        return new NotificationTemplateTranslation
        {
            VersionId = versionId,
            LanguageCode = languageCode.ToLowerInvariant(),
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = string.IsNullOrWhiteSpace(bodyText) ? null : bodyText
        };
    }

    internal void Update(string subject, string bodyHtml, string? bodyText, Guid modifiedBy)
    {
        Subject = subject;
        BodyHtml = bodyHtml;
        BodyText = string.IsNullOrWhiteSpace(bodyText) ? null : bodyText;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}
