using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an audit log entry for tracking system events.
/// </summary>
public class AuditLog : EntityBase
{
    /// <summary>
    /// Gets the ID of the user who performed the action (null for system actions).
    /// </summary>
    public Guid? UserId { get; private set; }

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
    /// Gets whether the action was successful.
    /// </summary>
    public bool IsSuccess { get; private set; }

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
        bool isSuccess,
        string? errorMessage,
        DateTime timestamp,
        string? correlationId) : base(id)
    {
        UserId = userId;
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

    public static AuditLog CreateSuccess(
        string actionType,
        string action,
        Guid? userId = null,
        Guid? applicationId = null,
        string? entityType = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null,
        string? correlationId = null)
    {
        return new AuditLog
        {
            UserId = userId,
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

    public static AuditLog CreateFailure(
        string actionType,
        string action,
        string errorMessage,
        Guid? userId = null,
        Guid? applicationId = null,
        string? entityType = null,
        Guid? entityId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null,
        string? correlationId = null)
    {
        return new AuditLog
        {
            UserId = userId,
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
