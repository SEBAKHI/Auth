using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an email verification token for OTP-based email confirmation.
/// </summary>
public class EmailVerificationToken : EntityBase
{
    /// <summary>
    /// Maximum number of verification attempts allowed per OTP.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Gets the ID of the user this token belongs to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the Argon2id hash of the 6-digit OTP.
    /// The actual OTP is sent to the user; only the hash is stored.
    /// </summary>
    public string OtpHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the email address the OTP was sent to.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this token expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this token was used (null if not yet used).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the number of verification attempts made.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this token was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets whether this token is valid (not expired, not used, and attempts not exceeded).
    /// </summary>
    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow && AttemptCount < MaxAttempts;

    /// <summary>
    /// Gets whether this token has been used.
    /// </summary>
    public bool IsUsed => UsedAt.HasValue;

    /// <summary>
    /// Gets whether this token has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt <= DateTime.UtcNow;

    /// <summary>
    /// Gets whether the maximum attempts have been reached.
    /// </summary>
    public bool IsMaxAttemptsReached => AttemptCount >= MaxAttempts;

    private EmailVerificationToken() : base()
    {
    }

    public EmailVerificationToken(
        Guid id,
        Guid userId,
        string otpHash,
        string email,
        DateTime expiresAt,
        DateTime? usedAt,
        int attemptCount,
        DateTime createdAt) : base(id)
    {
        UserId = userId;
        OtpHash = otpHash;
        Email = email;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new email verification token.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="otpHash">The Argon2id hash of the OTP.</param>
    /// <param name="email">The email address.</param>
    /// <param name="expirationMinutes">Token expiration in minutes (default 15).</param>
    public static EmailVerificationToken Create(
        Guid userId,
        string otpHash,
        string email,
        int expirationMinutes = 15)
    {
        var now = DateTime.UtcNow;
        return new EmailVerificationToken
        {
            UserId = userId,
            OtpHash = otpHash,
            Email = email.ToLowerInvariant(),
            ExpiresAt = now.AddMinutes(expirationMinutes),
            UsedAt = null,
            AttemptCount = 0,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Marks this token as used.
    /// </summary>
    public void MarkAsUsed()
    {
        if (UsedAt.HasValue)
        {
            throw new InvalidOperationException("Token has already been used.");
        }

        UsedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Increments the attempt count.
    /// </summary>
    public void IncrementAttempts()
    {
        AttemptCount++;
    }
}
