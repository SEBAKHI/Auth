using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for notification template operations: full-aggregate
/// admin CRUD plus the lean published-content read path used when sending.
/// </summary>
public interface INotificationTemplateRepository
{
    #region Admin CRUD (full aggregate)

    /// <summary>
    /// Gets a template aggregate (all versions with their translations) by ID.
    /// </summary>
    Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a template already exists for the (application, type, channel) scope.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid notificationTypeId,
        Guid? applicationId,
        NotificationChannelType channel,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new template with its versions and translations in one transaction.
    /// </summary>
    Task<NotificationTemplate> CreateAsync(NotificationTemplate template, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the aggregate state in one transaction: updates the template row
    /// (pointers, audit), inserts new versions, and syncs the draft version's
    /// translations (published/historical versions are immutable). Removes
    /// versions that were discarded from the aggregate.
    /// </summary>
    Task UpdateAsync(NotificationTemplate template, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a publish transition only while the database still points at the
    /// reviewed draft version and saved revision.
    /// </summary>
    Task<bool> TryPublishAsync(
        NotificationTemplate template,
        Guid expectedDraftVersionId,
        DateTime expectedRevisionAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists an unpublish transition only while the database still points at
    /// the reviewed published version.
    /// </summary>
    Task<bool> TryUnpublishAsync(
        NotificationTemplate template,
        Guid expectedPublishedVersionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a template with all its versions and translations in one transaction.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a paginated admin list with type/application display fields.
    /// </summary>
    Task<(IReadOnlyList<NotificationTemplateListItem> Templates, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? notificationTypeId,
        Guid? applicationId,
        NotificationChannelType? channel,
        bool? isPublished,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken);

    #endregion

    #region Send path (published content only)

    /// <summary>
    /// Gets the published version content (all translations) for the exact
    /// (type code, channel, application) scope, or null when the scope has no
    /// published template. The caller performs app-to-global fallback by probing
    /// the application scope first and the null scope second.
    /// </summary>
    Task<NotificationTemplateRenderSource?> GetPublishedAsync(
        string typeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the codes of system notification types that have no published
    /// global template for the given channel (startup health check).
    /// </summary>
    Task<IReadOnlyList<string>> GetSystemTypeCodesMissingPublishedGlobalTemplateAsync(
        NotificationChannelType channel,
        CancellationToken cancellationToken);

    #endregion
}
