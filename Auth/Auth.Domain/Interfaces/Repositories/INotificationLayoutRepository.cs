using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Notifications;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for notification layout operations.
/// </summary>
public interface INotificationLayoutRepository
{
    /// <summary>
    /// Gets all layouts ordered by scope (global first) and name.
    /// </summary>
    Task<IReadOnlyList<NotificationLayout>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a layout by its ID.
    /// </summary>
    Task<NotificationLayout?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a layout already exists for the (application, channel) scope.
    /// </summary>
    Task<bool> ExistsAsync(Guid? applicationId, NotificationChannelType channel, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new layout.
    /// </summary>
    Task<NotificationLayout> CreateAsync(NotificationLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing layout (draft columns, publish columns, audit).
    /// </summary>
    Task UpdateAsync(NotificationLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the published layout content for the exact (channel, application)
    /// scope, or null when none is published there. The caller performs
    /// app-to-global fallback by probing the application scope first.
    /// </summary>
    Task<NotificationLayoutRenderSource?> GetPublishedAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken);
}
