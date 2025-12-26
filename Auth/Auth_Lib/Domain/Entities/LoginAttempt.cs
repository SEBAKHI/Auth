using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a login attempt record for security monitoring.
/// </summary>
public class LoginAttempt : EntityBase
{
    /// <summary>
    /// Gets the ID of the user (if identified).
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Gets the email address used in the attempt.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the attempt was successful.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Gets the failure reason if unsuccessful.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Gets the IP address from which the attempt was made.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the user agent string.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Gets the approximate location based on IP.
    /// </summary>
    public string? Location { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the attempt.
    /// </summary>
    public DateTime AttemptedAt { get; private set; }

    /// <summary>
    /// Gets the application ID if available.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    private LoginAttempt() : base()
    {
    }

    public LoginAttempt(
        Guid id,
        Guid? userId,
        string email,
        bool isSuccess,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        string? location,
        DateTime attemptedAt,
        Guid? applicationId) : base(id)
    {
        UserId = userId;
        Email = email;
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Location = location;
        AttemptedAt = attemptedAt;
        ApplicationId = applicationId;
    }

    public static LoginAttempt CreateSuccess(
        Guid userId,
        string email,
        string? ipAddress,
        string? userAgent,
        string? location = null,
        Guid? applicationId = null)
    {
        return new LoginAttempt
        {
            UserId = userId,
            Email = email,
            IsSuccess = true,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = location,
            AttemptedAt = DateTime.UtcNow,
            ApplicationId = applicationId
        };
    }

    public static LoginAttempt CreateFailure(
        string email,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        Guid? userId = null,
        string? location = null,
        Guid? applicationId = null)
    {
        return new LoginAttempt
        {
            UserId = userId,
            Email = email,
            IsSuccess = false,
            FailureReason = failureReason,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = location,
            AttemptedAt = DateTime.UtcNow,
            ApplicationId = applicationId
        };
    }
}
