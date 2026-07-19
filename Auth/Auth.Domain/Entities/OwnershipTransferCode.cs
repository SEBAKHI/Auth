using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a one-time code that confirms an organization ownership transfer.
/// The code is emailed to the prospective new owner; the current owner must
/// enter it, which proves both parties consent to the transfer.
/// </summary>
public class OwnershipTransferCode : EntityBase
{
    /// <summary>
    /// Maximum number of verification attempts allowed per code.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Gets the ID of the organization whose ownership is being transferred.
    /// </summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Gets the ID of the prospective new owner the code was emailed to.
    /// The code is only redeemable for a transfer to this exact user.
    /// </summary>
    public Guid TargetUserId { get; private set; }

    /// <summary>
    /// Gets the ID of the owner who initiated the transfer.
    /// </summary>
    public Guid InitiatedBy { get; private set; }

    /// <summary>
    /// Gets the Argon2id hash of the 6-digit code.
    /// The actual code is sent to the target user; only the hash is stored.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

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
    /// Gets whether this code is valid (not expired, not used, and attempts not exceeded).
    /// </summary>
    public bool IsValid => UsedAt == null && ExpiresAt > DateTime.UtcNow && AttemptCount < MaxAttempts;

    private OwnershipTransferCode() : base()
    {
    }

    public OwnershipTransferCode(
        Guid id,
        Guid organizationId,
        Guid targetUserId,
        Guid initiatedBy,
        string codeHash,
        DateTime expiresAt,
        DateTime? usedAt,
        int attemptCount,
        DateTime createdAt) : base(id)
    {
        OrganizationId = organizationId;
        TargetUserId = targetUserId;
        InitiatedBy = initiatedBy;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new ownership transfer code.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="targetUserId">The prospective new owner's user ID.</param>
    /// <param name="initiatedBy">The current owner's user ID.</param>
    /// <param name="codeHash">The Argon2id hash of the code.</param>
    /// <param name="expirationMinutes">Code expiration in minutes.</param>
    public static OwnershipTransferCode Create(
        Guid organizationId,
        Guid targetUserId,
        Guid initiatedBy,
        string codeHash,
        int expirationMinutes = 15)
    {
        var now = DateTime.UtcNow;
        return new OwnershipTransferCode
        {
            OrganizationId = organizationId,
            TargetUserId = targetUserId,
            InitiatedBy = initiatedBy,
            CodeHash = codeHash,
            ExpiresAt = now.AddMinutes(expirationMinutes),
            UsedAt = null,
            AttemptCount = 0,
            CreatedAt = now
        };
    }
}
