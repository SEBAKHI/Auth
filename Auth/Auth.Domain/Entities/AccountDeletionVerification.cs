using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a deletion re-authentication OTP: used by passwordless in-app
/// requests and by the public no-login deletion flow. Mirrors
/// <see cref="EmailVerificationToken"/> semantics (Argon2id hash, short expiry,
/// capped attempts). UserId is a loose reference: these rows belong to accounts
/// that are about to be deleted, so nothing may block or outlive the purge.
/// </summary>
public class AccountDeletionVerification : EntityBase
{
    /// <summary>
    /// Maximum number of verification attempts allowed per OTP.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Gets the ID of the user this verification targets (null only for
    /// legacy rows; every write sets it).
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Gets the email address the OTP was sent to.
    /// </summary>
    public Email Email { get; private set; } = Email.From(string.Empty);

    /// <summary>
    /// Gets the Argon2id hash of the 6-digit OTP.
    /// The actual OTP is sent to the user; only the hash is stored.
    /// </summary>
    public string OtpHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this code expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this code was used (null if not yet used).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the number of verification attempts made.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this code was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets whether this code is valid (not expired, not used, and attempts
    /// not exceeded).
    /// </summary>
    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow && AttemptCount < MaxAttempts;

    private AccountDeletionVerification() : base()
    {
    }

    public AccountDeletionVerification(
        Guid id,
        Guid? userId,
        string email,
        string otpHash,
        DateTime expiresAt,
        DateTime? usedAt,
        int attemptCount,
        DateTime createdAt) : base(id)
    {
        UserId = userId;
        Email = Email.From(email);
        OtpHash = otpHash;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new deletion verification code.
    /// </summary>
    /// <param name="userId">The targeted user.</param>
    /// <param name="email">The email the OTP is sent to.</param>
    /// <param name="otpHash">The Argon2id hash of the OTP.</param>
    /// <param name="expirationMinutes">Code expiration in minutes (default 15).</param>
    public static AccountDeletionVerification Create(
        Guid userId,
        string email,
        string otpHash,
        int expirationMinutes = 15)
    {
        var now = DateTime.UtcNow;
        return new AccountDeletionVerification
        {
            UserId = userId,
            Email = Email.From(email.ToLowerInvariant()),
            OtpHash = otpHash,
            ExpiresAt = now.AddMinutes(expirationMinutes),
            UsedAt = null,
            AttemptCount = 0,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Marks this code as used. Returns the generic OTP error when already
    /// used, keeping every failure shape indistinguishable.
    /// </summary>
    public ErrorOr<Success> MarkAsUsed()
    {
        if (UsedAt.HasValue)
        {
            return AccountDeletionErrors.InvalidOtp;
        }

        UsedAt = DateTime.UtcNow;
        return Result.Success;
    }

    /// <summary>
    /// Increments the attempt count.
    /// </summary>
    public void IncrementAttempts()
    {
        AttemptCount++;
    }
}
