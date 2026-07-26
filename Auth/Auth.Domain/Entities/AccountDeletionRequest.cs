using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a self-service account deletion request moving through the
/// two-phase deletion lifecycle: a grace window during which the account is
/// deactivated but recoverable via re-authentication, followed by staged,
/// irreversible destruction executed by the background worker. Terminal rows
/// are destruction evidence retained at least 3 years.
/// </summary>
public class AccountDeletionRequest : EntityBase
{
    /// <summary>
    /// Maximum length persisted for <see cref="LastError"/> (column limit).
    /// </summary>
    public const int MaxLastErrorLength = 2000;

    /// <summary>
    /// Gets the ID of the user this request belongs to. Loose reference: the
    /// row survives the user purge as destruction evidence.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the lifecycle state of the request.
    /// </summary>
    public AccountDeletionStatus Status { get; private set; }

    /// <summary>
    /// Gets where the request originated (in-app or public web).
    /// </summary>
    public AccountDeletionSource Source { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when deletion was requested (the compliance
    /// acknowledgment instant).
    /// </summary>
    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the grace window ends and the request
    /// becomes eligible for execution.
    /// </summary>
    public DateTime GraceEndsAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the user recovered the account (null unless
    /// cancelled).
    /// </summary>
    public DateTime? CancelledAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when staged destruction finished (null unless
    /// completed).
    /// </summary>
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the retention-policy version applied to this deletion (format
    /// "YYYY.MM").
    /// </summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the number of execution attempts made by the worker.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Gets the last execution failure diagnostic. Must never contain PII.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this row was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the actor who created the request (the user themselves for both
    /// sources; email possession authenticates the public flow).
    /// </summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>
    /// Gets whether the request still occupies the user's single active slot
    /// (enforced by the filtered unique index).
    /// </summary>
    public bool IsActive => Status is AccountDeletionStatus.PendingGrace or AccountDeletionStatus.Processing;

    private AccountDeletionRequest() : base()
    {
    }

    public AccountDeletionRequest(
        Guid id,
        Guid userId,
        AccountDeletionStatus status,
        AccountDeletionSource source,
        DateTime requestedAtUtc,
        DateTime graceEndsAtUtc,
        DateTime? cancelledAtUtc,
        DateTime? completedAtUtc,
        string policyVersion,
        int attemptCount,
        string? lastError,
        DateTime createdAt,
        Guid createdBy) : base(id)
    {
        UserId = userId;
        Status = status;
        Source = source;
        RequestedAtUtc = requestedAtUtc;
        GraceEndsAtUtc = graceEndsAtUtc;
        CancelledAtUtc = cancelledAtUtc;
        CompletedAtUtc = completedAtUtc;
        PolicyVersion = policyVersion;
        AttemptCount = attemptCount;
        LastError = lastError;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Creates a new pending deletion request whose grace window starts now.
    /// </summary>
    /// <param name="userId">The user requesting deletion.</param>
    /// <param name="source">Where the request originated.</param>
    /// <param name="gracePeriod">Length of the recovery window.</param>
    /// <param name="policyVersion">Retention-policy version in force.</param>
    /// <param name="createdBy">The requesting actor.</param>
    public static AccountDeletionRequest Create(
        Guid userId,
        AccountDeletionSource source,
        TimeSpan gracePeriod,
        string policyVersion,
        Guid createdBy)
    {
        var now = DateTime.UtcNow;
        return new AccountDeletionRequest
        {
            UserId = userId,
            Status = AccountDeletionStatus.PendingGrace,
            Source = source,
            RequestedAtUtc = now,
            GraceEndsAtUtc = now.Add(gracePeriod),
            PolicyVersion = policyVersion,
            AttemptCount = 0,
            CreatedAt = now,
            CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Claims the request for execution (worker only). Legal only while
    /// pending grace and after the grace window has elapsed.
    /// </summary>
    public ErrorOr<Success> Claim()
    {
        if (Status != AccountDeletionStatus.PendingGrace)
        {
            return AccountDeletionErrors.NotPendingGrace;
        }

        if (DateTime.UtcNow < GraceEndsAtUtc)
        {
            return AccountDeletionErrors.GraceNotElapsed;
        }

        Status = AccountDeletionStatus.Processing;
        return Result.Success;
    }

    /// <summary>
    /// Cancels the request (the user recovered the account). Deterministic
    /// after grace: once the worker has claimed the request, recovery is
    /// refused — the claim race has exactly one winner.
    /// </summary>
    public ErrorOr<Success> Cancel()
    {
        if (Status is AccountDeletionStatus.Processing
            or AccountDeletionStatus.Completed
            or AccountDeletionStatus.Failed)
        {
            return UserErrors.RecoveryWindowExpired;
        }

        if (Status != AccountDeletionStatus.PendingGrace)
        {
            return AccountDeletionErrors.NotPendingGrace;
        }

        Status = AccountDeletionStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        return Result.Success;
    }

    /// <summary>
    /// Marks staged destruction as finished (worker only).
    /// </summary>
    public ErrorOr<Success> Complete()
    {
        if (Status != AccountDeletionStatus.Processing)
        {
            return AccountDeletionErrors.NotProcessing;
        }

        Status = AccountDeletionStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        return Result.Success;
    }

    /// <summary>
    /// Records an execution failure: the request returns to the grace queue
    /// for retry, or dead-letters as Failed once <paramref name="maxAttempts"/>
    /// is reached.
    /// </summary>
    /// <param name="error">Diagnostic message (truncated to the column limit; must contain no PII).</param>
    /// <param name="maxAttempts">Attempt ceiling after which the request dead-letters.</param>
    public ErrorOr<Success> Fail(string error, int maxAttempts)
    {
        if (Status != AccountDeletionStatus.Processing)
        {
            return AccountDeletionErrors.NotProcessing;
        }

        AttemptCount++;
        LastError = error.Length <= MaxLastErrorLength ? error : error[..MaxLastErrorLength];
        Status = AttemptCount >= maxAttempts
            ? AccountDeletionStatus.Failed
            : AccountDeletionStatus.PendingGrace;
        return Result.Success;
    }
}
