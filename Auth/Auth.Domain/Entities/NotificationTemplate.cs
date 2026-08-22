using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Aggregate root for a notification template resolved by (application, type, channel).
/// Uses the pointer versioning model: <see cref="PublishedVersionId"/> and
/// <see cref="DraftVersionId"/> point into the owned version list, so publish and
/// rollback are single-pointer moves that carry every translation of the target
/// version atomically. ApplicationId = null designates the global fallback template.
/// </summary>
public class NotificationTemplate : AggregateRoot
{
    private readonly List<NotificationTemplateVersion> _versions = [];

    /// <summary>
    /// Gets the ID of the notification type this template renders.
    /// </summary>
    public Guid NotificationTypeId { get; private set; }

    /// <summary>
    /// Gets the owning application, or null for the global fallback template.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the delivery channel this template targets.
    /// </summary>
    public NotificationChannelType Channel { get; private set; }

    /// <summary>
    /// Gets the language whose translation is mandatory and used as the
    /// last-resort content fallback.
    /// </summary>
    public string DefaultLanguage { get; private set; } = Languages.Default;

    /// <summary>
    /// Gets the currently live version, or null when unpublished.
    /// </summary>
    public Guid? PublishedVersionId { get; private set; }

    /// <summary>
    /// Gets the pending draft version, or null when there are no unpublished edits.
    /// </summary>
    public Guid? DraftVersionId { get; private set; }

    /// <summary>
    /// Gets all versions of this template (history included).
    /// </summary>
    public IReadOnlyList<NotificationTemplateVersion> Versions => _versions.AsReadOnly();

    public NotificationTemplateVersion? PublishedVersion =>
        PublishedVersionId is null ? null : _versions.FirstOrDefault(v => v.Id == PublishedVersionId);

    public NotificationTemplateVersion? DraftVersion =>
        DraftVersionId is null ? null : _versions.FirstOrDefault(v => v.Id == DraftVersionId);

    public bool IsPublished => PublishedVersionId is not null;

    private NotificationTemplate() : base()
    {
    }

    public NotificationTemplate(
        Guid id,
        Guid notificationTypeId,
        Guid? applicationId,
        NotificationChannelType channel,
        string defaultLanguage,
        Guid? publishedVersionId,
        Guid? draftVersionId,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy,
        IEnumerable<NotificationTemplateVersion>? versions = null) : base(id)
    {
        NotificationTypeId = notificationTypeId;
        ApplicationId = applicationId;
        Channel = channel;
        DefaultLanguage = defaultLanguage;
        PublishedVersionId = publishedVersionId;
        DraftVersionId = draftVersionId;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;

        if (versions is not null)
        {
            _versions.AddRange(versions);
        }
    }

    /// <summary>
    /// Creates a new template with an empty draft version 1.
    /// </summary>
    public static ErrorOr<NotificationTemplate> Create(
        Guid notificationTypeId,
        Guid? applicationId,
        NotificationChannelType channel,
        string defaultLanguage,
        Guid createdBy)
    {
        var normalizedLanguage = Languages.Normalize(defaultLanguage);
        if (normalizedLanguage is null)
        {
            return NotificationErrors.UnsupportedLanguage(defaultLanguage);
        }

        var template = new NotificationTemplate
        {
            NotificationTypeId = notificationTypeId,
            ApplicationId = applicationId,
            Channel = channel,
            DefaultLanguage = normalizedLanguage
        };
        template.SetCreated(createdBy);

        var draft = NotificationTemplateVersion.Create(template.Id, 1, createdBy);
        template._versions.Add(draft);
        template.DraftVersionId = draft.Id;

        return template;
    }

    /// <summary>
    /// Returns the current draft version, lazily creating one (as a clone of the
    /// published version, or empty when never published) when none is pending.
    /// </summary>
    public NotificationTemplateVersion EnsureDraft(Guid userId)
    {
        if (DraftVersion is { } existingDraft)
        {
            return existingDraft;
        }

        var nextNumber = _versions.Count == 0 ? 1 : _versions.Max(v => v.VersionNumber) + 1;

        var draft = PublishedVersion is { } published
            ? published.CloneAsDraft(nextNumber, userId)
            : NotificationTemplateVersion.Create(Id, nextNumber, userId);

        _versions.Add(draft);
        DraftVersionId = draft.Id;
        SetModified(userId);
        return draft;
    }

    /// <summary>
    /// Adds or updates a translation on the draft version (creating the draft when needed).
    /// </summary>
    public ErrorOr<Success> UpsertTranslation(
        string languageCode,
        string subject,
        string bodyHtml,
        string? bodyText,
        Guid userId)
    {
        var normalized = Languages.Normalize(languageCode);
        if (normalized is null)
        {
            return NotificationErrors.UnsupportedLanguage(languageCode);
        }

        var draft = EnsureDraft(userId);
        draft.UpsertTranslation(normalized, subject, bodyHtml, bodyText, userId);
        SetModified(userId);
        return Result.Success;
    }

    /// <summary>
    /// Removes a translation from the draft version. The default language
    /// translation cannot be removed.
    /// </summary>
    public ErrorOr<Success> RemoveTranslation(string languageCode, Guid userId)
    {
        var normalized = Languages.Normalize(languageCode);
        if (normalized is null)
        {
            return NotificationErrors.UnsupportedLanguage(languageCode);
        }

        if (string.Equals(normalized, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return NotificationErrors.CannotRemoveDefaultLanguageTranslation(normalized);
        }

        var draft = EnsureDraft(userId);
        if (!draft.RemoveTranslation(normalized))
        {
            return NotificationErrors.TranslationNotFound(normalized);
        }

        SetModified(userId);
        return Result.Success;
    }

    /// <summary>
    /// Sets the change note on the current draft version.
    /// </summary>
    public ErrorOr<Success> SetDraftChangeNote(string? changeNote, Guid userId)
    {
        if (DraftVersion is not { } draft)
        {
            return NotificationErrors.NoDraftToPublish;
        }

        draft.SetChangeNote(changeNote);
        SetModified(userId);
        return Result.Success;
    }

    /// <summary>
    /// Verifies that the pending draft is the exact version reviewed by the caller.
    /// </summary>
    public ErrorOr<Success> ValidatePublishTarget(
        Guid expectedDraftVersionId,
        DateTime expectedRevisionAt)
    {
        if (DraftVersion is not { } draft)
        {
            return NotificationErrors.NoDraftToPublish;
        }

        return draft.Id == expectedDraftVersionId &&
               (ModifiedAt ?? CreatedAt) == expectedRevisionAt
            ? Result.Success
            : NotificationErrors.PublishTargetChanged;
    }

    /// <summary>
    /// Publishes the pending draft only when it is the exact version reviewed by
    /// the caller.
    /// </summary>
    public ErrorOr<Success> Publish(
        Guid expectedDraftVersionId,
        DateTime expectedRevisionAt,
        Guid userId)
    {
        var targetResult = ValidatePublishTarget(expectedDraftVersionId, expectedRevisionAt);
        if (targetResult.IsError)
        {
            return targetResult.Errors;
        }

        var draft = DraftVersion!;

        if (draft.FindTranslation(DefaultLanguage) is null)
        {
            return NotificationErrors.DefaultLanguageTranslationRequired(DefaultLanguage);
        }

        PublishedVersionId = draft.Id;
        DraftVersionId = null;
        SetModified(userId);

        RaiseDomainEvent(new NotificationTemplatePublishedEvent(
            Id, NotificationTypeId, ApplicationId, Channel, draft.Id, draft.VersionNumber, userId));

        return Result.Success;
    }

    /// <summary>
    /// Unpublishes the template. Forbidden for the global template of a system
    /// type because critical auth flows depend on it (<paramref name="isSystemType"/>
    /// comes from the owning NotificationType).
    /// </summary>
    public ErrorOr<Success> Unpublish(
        bool isSystemType,
        Guid expectedPublishedVersionId,
        Guid userId)
    {
        if (isSystemType && ApplicationId is null)
        {
            return NotificationErrors.CannotUnpublishSystemTemplate;
        }

        if (PublishedVersionId is not { } publishedId)
        {
            return NotificationErrors.NotPublished;
        }

        if (publishedId != expectedPublishedVersionId)
        {
            return NotificationErrors.UnpublishTargetChanged;
        }

        PublishedVersionId = null;
        SetModified(userId);

        RaiseDomainEvent(new NotificationTemplateUnpublishedEvent(
            Id, NotificationTypeId, ApplicationId, Channel, publishedId, userId));

        return Result.Success;
    }

    /// <summary>
    /// Rolls the published pointer back to a previous version; all translations
    /// of that version return together, with no cross-version mixing.
    /// </summary>
    public ErrorOr<Success> RollbackTo(Guid versionId, Guid userId)
    {
        var target = _versions.FirstOrDefault(v => v.Id == versionId);
        if (target is null)
        {
            return NotificationErrors.VersionNotFound(versionId);
        }

        if (target.Id == DraftVersionId)
        {
            // The draft is published through Publish (which validates it), not rollback.
            return NotificationErrors.NoDraftToPublish;
        }

        if (target.FindTranslation(DefaultLanguage) is null)
        {
            return NotificationErrors.DefaultLanguageTranslationRequired(DefaultLanguage);
        }

        var fromVersionId = PublishedVersionId;
        PublishedVersionId = target.Id;
        SetModified(userId);

        RaiseDomainEvent(new NotificationTemplateRolledBackEvent(
            Id, NotificationTypeId, ApplicationId, Channel,
            fromVersionId, target.Id, target.VersionNumber, userId));

        return Result.Success;
    }

    /// <summary>
    /// Creates a new draft as a copy of an arbitrary historical version. Fails when
    /// a draft is already pending so unsaved edits are never silently discarded.
    /// </summary>
    public ErrorOr<NotificationTemplateVersion> CreateDraftFromVersion(Guid versionId, Guid userId)
    {
        if (DraftVersionId is not null)
        {
            return NotificationErrors.DraftAlreadyExists;
        }

        var source = _versions.FirstOrDefault(v => v.Id == versionId);
        if (source is null)
        {
            return NotificationErrors.VersionNotFound(versionId);
        }

        var nextNumber = _versions.Max(v => v.VersionNumber) + 1;
        var draft = source.CloneAsDraft(nextNumber, userId);
        _versions.Add(draft);
        DraftVersionId = draft.Id;
        SetModified(userId);
        return draft;
    }

    /// <summary>
    /// Discards the pending draft version (its translations are removed with it).
    /// </summary>
    public ErrorOr<Guid> DiscardDraft(Guid userId)
    {
        if (DraftVersion is not { } draft)
        {
            return NotificationErrors.NoDraftToDiscard;
        }

        _versions.Remove(draft);
        DraftVersionId = null;
        SetModified(userId);
        return draft.Id;
    }

    /// <summary>
    /// Whether this template may be deleted (<paramref name="isSystemType"/> comes
    /// from the owning NotificationType). App-scoped overrides of system types are
    /// deletable because the global fallback always exists.
    /// </summary>
    public ErrorOr<Success> EnsureDeletable(bool isSystemType)
    {
        if (isSystemType && ApplicationId is null)
        {
            return NotificationErrors.CannotDeleteSystemGlobalTemplate;
        }

        return Result.Success;
    }
}
