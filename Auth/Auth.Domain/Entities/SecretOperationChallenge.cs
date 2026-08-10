using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Step-up re-authentication for a destructive secret operation: a one-time
/// code emailed to the administrator who asked for it, which must be entered
/// before the operation is even costed, let alone executed.
/// </summary>
/// <remarks>
/// <para>
/// Two windows, deliberately different. <see cref="ExpiresAt"/> bounds how long
/// the emailed code may be entered (the address may be a shared mailbox, so it
/// is short but human). <see cref="ApprovalExpiresAt"/> bounds how long the
/// verified challenge stays spendable — an approval left open in a browser tab
/// is a signed blank cheque against every token the platform has issued, so it
/// dies in minutes whatever the code window was.
/// </para>
/// <para>
/// The challenge is bound to three things at once: the administrator
/// (<see cref="RequestedBy"/>), the exact <see cref="Operation"/>, and — for
/// imports — a digest of the key material (<see cref="PayloadHash"/>). All
/// three are re-checked at spend time, so an approval cannot be moved to
/// another admin, another key, or other key material after the fact.
/// </para>
/// </remarks>
public class SecretOperationChallenge : EntityBase
{
    /// <summary>
    /// Maximum number of verification attempts allowed per challenge. A sixth
    /// attempt is refused even with the right code; the admin must request a
    /// fresh one, which invalidates whatever the guesser was working against.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Minutes a verified challenge stays spendable. Deliberately not a
    /// configurable setting: the whole point is that it is too short to leave
    /// lying around, and an operator who can widen it has removed the control.
    /// </summary>
    public const int ApprovalWindowMinutes = 5;

    /// <summary>
    /// Gets the administrator who requested the operation. The code is emailed
    /// to this account and only this account may verify or spend the challenge.
    /// </summary>
    public Guid RequestedBy { get; private set; }

    /// <summary>
    /// Gets the operation this challenge authorizes, and nothing else.
    /// </summary>
    public SecretOperation Operation { get; private set; }

    /// <summary>
    /// Gets the SHA-256 digest of the key material to be imported, or null for
    /// the generate operations, which carry no caller-supplied payload.
    /// Re-checked at spend time so approved material cannot be swapped.
    /// </summary>
    public string? PayloadHash { get; private set; }

    /// <summary>
    /// Gets the Argon2id hash of the 6-digit code. The code itself exists only
    /// in the email that carried it.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp after which the code can no longer be entered.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the code was accepted (null while unverified).
    /// </summary>
    public DateTime? VerifiedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp after which a verified challenge is no longer
    /// spendable (null while unverified).
    /// </summary>
    public DateTime? ApprovalExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the challenge was spent on an operation
    /// (null if not yet spent).
    /// </summary>
    public DateTime? UsedAt { get; private set; }

    /// <summary>
    /// Gets the number of verification attempts made.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the client address the challenge was requested from, recorded for
    /// the audit trail. Never used as an authorization input — a proxy hop can
    /// change it legitimately mid-flow.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this challenge was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets whether the code may still be entered: unspent, unverified,
    /// unexpired, and under the attempt cap.
    /// </summary>
    public bool IsOpen =>
        UsedAt == null
        && VerifiedAt == null
        && ExpiresAt > DateTime.UtcNow
        && AttemptCount < MaxAttempts;

    /// <summary>
    /// Gets whether this challenge is a live approval: verified, unspent, and
    /// inside the approval window.
    /// </summary>
    public bool IsApproved =>
        UsedAt == null
        && VerifiedAt != null
        && ApprovalExpiresAt > DateTime.UtcNow;

    private SecretOperationChallenge() : base()
    {
    }

    public SecretOperationChallenge(
        Guid id,
        Guid requestedBy,
        SecretOperation operation,
        string? payloadHash,
        string codeHash,
        DateTime expiresAt,
        DateTime? verifiedAt,
        DateTime? approvalExpiresAt,
        DateTime? usedAt,
        int attemptCount,
        string? ipAddress,
        DateTime createdAt) : base(id)
    {
        RequestedBy = requestedBy;
        Operation = operation;
        PayloadHash = payloadHash;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        VerifiedAt = verifiedAt;
        ApprovalExpiresAt = approvalExpiresAt;
        UsedAt = usedAt;
        AttemptCount = attemptCount;
        IpAddress = ipAddress;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new challenge for an operation.
    /// </summary>
    /// <param name="requestedBy">The administrator requesting the operation.</param>
    /// <param name="operation">The operation being authorized.</param>
    /// <param name="payloadHash">Digest of the key material for imports; null for generates.</param>
    /// <param name="codeHash">The Argon2id hash of the emailed code.</param>
    /// <param name="ipAddress">The requesting client address, for the audit trail.</param>
    /// <param name="expirationMinutes">Code entry window in minutes.</param>
    public static SecretOperationChallenge Create(
        Guid requestedBy,
        SecretOperation operation,
        string? payloadHash,
        string codeHash,
        string? ipAddress,
        int expirationMinutes = 15)
    {
        var now = DateTime.UtcNow;
        return new SecretOperationChallenge
        {
            RequestedBy = requestedBy,
            Operation = operation,
            PayloadHash = payloadHash,
            CodeHash = codeHash,
            ExpiresAt = now.AddMinutes(expirationMinutes),
            VerifiedAt = null,
            ApprovalExpiresAt = null,
            UsedAt = null,
            AttemptCount = 0,
            IpAddress = ipAddress,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Records a correct code and opens the approval window. Returns the single
    /// generic code error when the challenge is no longer open, so a caller
    /// cannot tell a wrong code from a spent, expired or exhausted one.
    /// </summary>
    public ErrorOr<Success> MarkVerified()
    {
        if (!IsOpen)
        {
            return SecretErrors.InvalidChallengeCode;
        }

        var now = DateTime.UtcNow;
        VerifiedAt = now;
        ApprovalExpiresAt = now.AddMinutes(ApprovalWindowMinutes);
        return Result.Success;
    }

    /// <summary>
    /// Increments the attempt count after a rejected code.
    /// </summary>
    public void IncrementAttempts()
    {
        AttemptCount++;
    }

    /// <summary>
    /// Checks that this approval may be spent on the given operation by the
    /// given administrator with the given key material. Every failure shape
    /// collapses to one error: the caller learns that the approval is not
    /// usable, never which of the bindings it missed.
    /// </summary>
    /// <param name="operation">The operation about to execute.</param>
    /// <param name="payloadHash">Digest of the key material being submitted; null for generates.</param>
    /// <param name="requestedBy">The administrator executing the operation.</param>
    public ErrorOr<Success> EnsureSpendableFor(
        SecretOperation operation,
        string? payloadHash,
        Guid requestedBy)
    {
        if (!IsApproved
            || RequestedBy != requestedBy
            || Operation != operation
            || !string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return SecretErrors.ChallengeNotApproved;
        }

        return Result.Success;
    }

    /// <summary>
    /// Marks the approval spent. Idempotence is enforced in the repository by a
    /// conditional update; this keeps the in-memory entity honest.
    /// </summary>
    public ErrorOr<Success> MarkUsed()
    {
        if (UsedAt.HasValue)
        {
            return SecretErrors.ChallengeNotApproved;
        }

        UsedAt = DateTime.UtcNow;
        return Result.Success;
    }
}
