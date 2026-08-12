using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// One sign-in ceremony, from the credentials being presented to the outcome
/// being known — not one HTTP request. A two-factor sign-in spans two requests,
/// and produces one row: opened by <see cref="CreateChallenged"/> when the
/// challenge is issued, settled later by the repository once the second factor
/// succeeds, is exhausted, or the ceremony is abandoned and simply ages out.
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
    public Email Email { get; private set; } = Email.From(string.Empty);

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

    /// <summary>
    /// Gets the two-factor challenge this ceremony is waiting on, or null when
    /// the sign-in never involved a second factor. It is the handle the verify
    /// step uses to settle this row instead of writing a second one.
    /// </summary>
    public Guid? TwoFactorChallengeId { get; private set; }

    /// <summary>
    /// Gets whether the ceremony is still open: a second factor was demanded and
    /// nothing has settled it. Once the challenge lifetime has passed with this
    /// still true, the ceremony was abandoned or blocked.
    /// </summary>
    public bool IsAwaitingSecondFactor =>
        TwoFactorChallengeId.HasValue && !IsSuccess && FailureReason is null;

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
        Guid? applicationId,
        Guid? twoFactorChallengeId = null) : base(id)
    {
        UserId = userId;
        Email = Email.From(email);
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Location = location;
        AttemptedAt = attemptedAt;
        ApplicationId = applicationId;
        TwoFactorChallengeId = twoFactorChallengeId;
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
            Email = Email.From(email),
            IsSuccess = true,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = location,
            AttemptedAt = DateTime.UtcNow,
            ApplicationId = applicationId
        };
    }

    /// <summary>
    /// Opens a ceremony: the primary factor was accepted and a second factor has
    /// been demanded. Nothing has been rejected and nothing has been issued, so
    /// there is deliberately no failure-reason parameter — a challenge cannot be
    /// recorded with a reason, and a settled row cannot be recorded without one.
    /// </summary>
    public static LoginAttempt CreateChallenged(
        Guid userId,
        string email,
        Guid challengeId,
        string? ipAddress,
        string? userAgent,
        string? location = null,
        Guid? applicationId = null)
    {
        return new LoginAttempt
        {
            UserId = userId,
            Email = Email.From(email),
            IsSuccess = false,
            FailureReason = null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = location,
            AttemptedAt = DateTime.UtcNow,
            ApplicationId = applicationId,
            TwoFactorChallengeId = challengeId
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
            Email = Email.From(email),
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
