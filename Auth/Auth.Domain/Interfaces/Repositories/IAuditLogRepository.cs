using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for audit log operations.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Creates a new audit log entry.
    /// </summary>
    Task CreateAsync(AuditLog auditLog, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an audit log by its ID.
    /// </summary>
    Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets audit logs with filtering and pagination. <paramref name="sortBy"/>
    /// accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.AuditLogs"/>; null keeps the default order.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? userId,
        Guid? applicationId,
        string? action,
        DateTime? fromDate,
        DateTime? toDate,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets audit logs for a specific entity. <paramref name="sortBy"/> accepts
    /// the allow-listed field names in <see cref="Constants.SortFields.AuditLogs"/>.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    // GetByCorrelationIdAsync used to sit here. Its implementation returned an
    // empty list unconditionally under a "placeholder" comment, AuditLogs has no
    // CorrelationId column to query, and nothing ever called it.

    /// <summary>
    /// Cleans up old audit logs.
    /// </summary>
    Task CleanupOldLogsAsync(DateTime olderThan, CancellationToken cancellationToken);
}
