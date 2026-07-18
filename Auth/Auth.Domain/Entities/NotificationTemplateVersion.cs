using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// An immutable snapshot of a template's content across all languages. A version
/// owns its complete set of translations; publishing or rolling back a version
/// moves every translation together, guaranteeing cross-language consistency.
/// Only the version currently pointed to by the template's DraftVersionId is mutable.
/// </summary>
public class NotificationTemplateVersion : EntityBase
{
    private readonly List<NotificationTemplateTranslation> _translations = [];

    /// <summary>
    /// Gets the ID of the owning template.
    /// </summary>
    public Guid TemplateId { get; private set; }

    /// <summary>
    /// Gets the sequential version number (unique per template).
    /// </summary>
    public int VersionNumber { get; private set; }

    /// <summary>
    /// Gets the optional author note describing what changed in this version.
    /// </summary>
    public string? ChangeNote { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    /// <summary>
    /// Gets the translations belonging to this version.
    /// </summary>
    public IReadOnlyList<NotificationTemplateTranslation> Translations => _translations.AsReadOnly();

    private NotificationTemplateVersion() : base()
    {
    }

    public NotificationTemplateVersion(
        Guid id,
        Guid templateId,
        int versionNumber,
        string? changeNote,
        DateTime createdAt,
        Guid createdBy,
        IEnumerable<NotificationTemplateTranslation>? translations = null) : base(id)
    {
        TemplateId = templateId;
        VersionNumber = versionNumber;
        ChangeNote = changeNote;
        CreatedAt = createdAt;
        CreatedBy = createdBy;

        if (translations is not null)
        {
            _translations.AddRange(translations);
        }
    }

    internal static NotificationTemplateVersion Create(
        Guid templateId,
        int versionNumber,
        Guid createdBy,
        string? changeNote = null)
    {
        return new NotificationTemplateVersion
        {
            TemplateId = templateId,
            VersionNumber = versionNumber,
            ChangeNote = changeNote,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Creates a new draft version copying every translation of this version.
    /// </summary>
    internal NotificationTemplateVersion CloneAsDraft(int newVersionNumber, Guid createdBy)
    {
        var draft = Create(TemplateId, newVersionNumber, createdBy);

        foreach (var translation in _translations)
        {
            draft._translations.Add(NotificationTemplateTranslation.Create(
                draft.Id,
                translation.LanguageCode,
                translation.Subject,
                translation.BodyHtml,
                translation.BodyText));
        }

        return draft;
    }

    internal NotificationTemplateTranslation? FindTranslation(string languageCode) =>
        _translations.FirstOrDefault(t =>
            string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));

    internal void UpsertTranslation(
        string languageCode,
        string subject,
        string bodyHtml,
        string? bodyText,
        Guid userId)
    {
        var existing = FindTranslation(languageCode);
        if (existing is null)
        {
            _translations.Add(NotificationTemplateTranslation.Create(
                Id, languageCode, subject, bodyHtml, bodyText));
        }
        else
        {
            existing.Update(subject, bodyHtml, bodyText, userId);
        }
    }

    internal bool RemoveTranslation(string languageCode)
    {
        var existing = FindTranslation(languageCode);
        return existing is not null && _translations.Remove(existing);
    }

    internal void SetChangeNote(string? changeNote)
    {
        ChangeNote = changeNote;
    }
}
