using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an audit log entry for tracking system events.
/// </summary>
public class AuditLog : EntityBase
{
    /// <summary>
    /// Gets the ID of the user the action HAPPENED TO — the subject.
    /// </summary>
    /// <remarks>
    /// Not the actor. When an administrator locks someone's account, the subject
    /// is the locked account and <see cref="PerformedBy"/> is the administrator.
    /// The two are the same only when a person acts on their own account, and
    /// both are null for a system action with no human on either side.
    /// Conflating them is what makes an audit trail unable to answer the one
    /// question it exists for, so the distinction is stated here rather than left
    /// to each caller to remember.
    /// </remarks>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Gets the ID of the user who PERFORMED the action — the actor.
    /// </summary>
    public Guid? PerformedBy { get; private set; }

    /// <summary>
    /// Gets the session the action was performed from, when it came from one.
    /// </summary>
    public Guid? SessionId { get; private set; }

    /// <summary>
    /// Gets the ID of the application where the action occurred.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the type/category of the action (e.g., "Authentication", "UserManagement").
    /// </summary>
    public string ActionType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the specific action performed (e.g., "Login", "CreateUser").
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the type of entity affected (e.g., "User", "Role").
    /// </summary>
    public string? EntityType { get; private set; }

    /// <summary>
    /// Gets the ID of the entity affected.
    /// </summary>
    public Guid? EntityId { get; private set; }

    /// <summary>
    /// Gets the old values before the change (JSON).
    /// </summary>
    public string? OldValues { get; private set; }

    /// <summary>
    /// Gets the new values after the change (JSON).
    /// </summary>
    public string? NewValues { get; private set; }

    /// <summary>
    /// Gets the IP address from which the action was performed.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the user agent string.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Gets additional details about the action (JSON).
    /// </summary>
    public string? AdditionalData { get; private set; }

    /// <summary>
    /// Gets whether the action succeeded, or null when the row predates the
    /// column and the outcome was never recorded.
    /// </summary>
    /// <remarks>
    /// Nullable so that "we do not know" is expressible. It has to be: for the
    /// whole life of the table before this column existed, the read path
    /// returned true for every row regardless of what happened, so a reader had
    /// no way to tell a success from an event that was never asked about.
    /// </remarks>
    public bool? IsSuccess { get; private set; }

    /// <summary>
    /// Gets the error message if the action failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the action occurred.
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    /// Gets the correlation ID for tracing related events.
    /// </summary>
    public string? CorrelationId { get; private set; }

    private AuditLog() : base()
    {
    }

    public AuditLog(
        Guid id,
        Guid? userId,
        Guid? applicationId,
        string actionType,
        string action,
        string? entityType,
        Guid? entityId,
        string? oldValues,
        string? newValues,
        string? ipAddress,
        string? userAgent,
        string? additionalData,
        bool? isSuccess,
        string? errorMessage,
        DateTime timestamp,
        string? correlationId,
        Guid? performedBy = null,
        Guid? sessionId = null) : base(id)
    {
        UserId = userId;
        PerformedBy = performedBy;
        SessionId = sessionId;
        ApplicationId = applicationId;
        ActionType = actionType;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OldValues = oldValues;
        NewValues = newValues;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        AdditionalData = additionalData;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Timestamp = timestamp;
        CorrelationId = correlationId;
    }

    /// <param name="userId">Who it happened TO. See <see cref="UserId"/>.</param>
    /// <param name="performedBy">Who DID it. See <see cref="PerformedBy"/>.</param>
    public static AuditLog CreateSuccess(
        string actionType,
        string action,
        Guid? userId = null,
        Guid? performedBy = null,
        Guid? applicationId = null,
        string? entityType = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null,
        string? correlationId = null,
        Guid? sessionId = null)
    {
        return new AuditLog
        {
            UserId = userId,
            PerformedBy = performedBy,
            SessionId = sessionId,
            ApplicationId = applicationId,
            ActionType = actionType,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AdditionalData = additionalData,
            IsSuccess = true,
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId
        };
    }

    /// <param name="userId">Who it happened TO. See <see cref="UserId"/>.</param>
    /// <param name="performedBy">Who DID it. See <see cref="PerformedBy"/>.</param>
    public static AuditLog CreateFailure(
        string actionType,
        string action,
        string errorMessage,
        Guid? userId = null,
        Guid? performedBy = null,
        Guid? applicationId = null,
        string? entityType = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null,
        string? correlationId = null,
        Guid? sessionId = null)
    {
        return new AuditLog
        {
            UserId = userId,
            PerformedBy = performedBy,
            SessionId = sessionId,
            OldValues = oldValues,
            NewValues = newValues,
            ApplicationId = applicationId,
            ActionType = actionType,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AdditionalData = additionalData,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId
        };
    }
}
