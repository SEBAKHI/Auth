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
    /// <param name="participantId">
    /// A person to narrow to, on the side named by <paramref name="participantRole"/>.
    /// This replaced a bare <c>userId</c>, which could only ever mean the subject
    /// — so an operator's own actions were unreachable through the one filter
    /// that took a person.
    /// </param>
    /// <param name="participantRole">
    /// Which side <paramref name="participantId"/> has to be on. Null narrows to
    /// the subject: when the caller has not said, the answer must be the
    /// narrower one, never the wider.
    /// </param>
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? participantId,
        Enums.AuditParticipantRole? participantRole,
        Guid? applicationId,
        string? action,
        string? actionType,
        bool? isSuccess,
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
