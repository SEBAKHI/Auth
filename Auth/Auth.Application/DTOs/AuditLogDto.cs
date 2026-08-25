namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for audit log entries.
/// </summary>
public class AuditLogDto
{
    public Guid Id { get; set; }

    /// <summary>Who the action happened TO — the subject, not the actor.</summary>
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }

    /// <summary>Who PERFORMED the action — the actor, when it differs from the subject.</summary>
    public Guid? PerformedBy { get; set; }
    public string? PerformedByName { get; set; }
    public string? PerformedByEmail { get; set; }

    public Guid? SessionId { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? AdditionalData { get; set; }

    /// <summary>
    /// Whether the action succeeded, or null when the row predates the column
    /// and the outcome was never recorded. Callers must render the three states
    /// as three states: a null shown as a success is the defect this replaced.
    /// </summary>
    public bool? IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Paginated result for audit logs.
/// </summary>
public class PagedAuditLogsDto
{
    public IReadOnlyList<AuditLogDto> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
