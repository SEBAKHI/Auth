using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for notification type lookups and metadata updates.
/// </summary>
public interface INotificationTypeRepository
{
    /// <summary>
    /// Gets all notification types ordered by name.
    /// </summary>
    Task<IReadOnlyList<NotificationType>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a notification type by its ID.
    /// </summary>
    Task<NotificationType?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a notification type by its stable code.
    /// </summary>
    Task<NotificationType?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the admin-editable metadata of a notification type.
    /// </summary>
    Task UpdateAsync(NotificationType type, CancellationToken cancellationToken);
}
