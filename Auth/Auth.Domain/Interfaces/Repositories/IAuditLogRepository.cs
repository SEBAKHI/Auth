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
    /// Gets audit logs with filtering and pagination.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? userId,
        Guid? applicationId,
        string? actionType,
        string? action,
        DateTime? fromDate,
        DateTime? toDate,
        bool? isSuccess,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets audit logs for a specific entity.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets audit logs by correlation ID.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up old audit logs.
    /// </summary>
    Task CleanupOldLogsAsync(DateTime olderThan, CancellationToken cancellationToken);
}
