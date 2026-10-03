using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// A one-time code emailed to an account's confirmed address before the account
/// binds its FIRST second factor. Whoever holds only the password cannot bind an
/// authenticator of their own to an account that has none: they would need the
/// mailbox as well.
/// </summary>
/// <remarks>
/// It proves possession of the mailbox for that bind and nothing else. It is never
/// a second factor, counts toward no sign-in, and is spent inside the transaction
/// that switches the factor on, so one code binds at most one factor.
/// </remarks>
public class TwoFactorBindCode : EntityBase
{
    /// <summary>
    /// Maximum number of verification attempts allowed per code.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Gets the ID of the account the code was emailed to.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the keyed hash of the 6-digit code. The code itself is only ever in
    /// the email.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this code expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this code was spent or superseded (null while live).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the number of verification attempts counted against this code.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the client address the code was requested from, for audit only.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this code was issued.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private TwoFactorBindCode() : base()
    {
    }

    public TwoFactorBindCode(
        Guid id,
        Guid userId,
        string codeHash,
        DateTime expiresAt,
        DateTime? usedAt,
        int attemptCount,
        string? ipAddress,
        DateTime createdAt) : base(id)
    {
        UserId = userId;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        IpAddress = ipAddress;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Issues a new code for an account.
    /// </summary>
    /// <param name="userId">The account binding its first second factor.</param>
    /// <param name="codeHash">The keyed hash of the code.</param>
    /// <param name="ipAddress">The requesting client address, for audit.</param>
    /// <param name="issuedAtUtc">The issue time, from the application clock.</param>
    /// <param name="expirationMinutes">How long the code may be entered.</param>
    public static TwoFactorBindCode Issue(
        Guid userId,
        string codeHash,
        string? ipAddress,
        DateTime issuedAtUtc,
        int expirationMinutes)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);

        return new TwoFactorBindCode
        {
            UserId = userId,
            CodeHash = codeHash,
            ExpiresAt = issuedAtUtc.AddMinutes(expirationMinutes),
            UsedAt = null,
            AttemptCount = 0,
            IpAddress = ipAddress,
            CreatedAt = issuedAtUtc
        };
    }
}
