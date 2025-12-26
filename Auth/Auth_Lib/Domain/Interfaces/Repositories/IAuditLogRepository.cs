using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for audit log operations.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Creates a new audit log entry.
    /// </summary>
    Task CreateAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit logs with filtering and pagination.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? userId = null,
        Guid? applicationId = null,
        string? actionType = null,
        string? action = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        bool? isSuccess = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit logs for a specific entity.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit logs by correlation ID.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old audit logs.
    /// </summary>
    Task CleanupOldLogsAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
