namespace Auth.Domain.ReadModels.Authentication;

/// <summary>
/// One entry in a user's own sign-in history.
///
/// A read model rather than the <c>LoginAttempt</c> entity because
/// <see cref="SecondFactorAttempts"/> belongs to the two-factor challenge, not to
/// the attempt: the entity never writes it and could not keep it correct, so
/// hanging it off the aggregate would be a field the aggregate cannot own.
/// </summary>
public sealed record SignInHistoryEntry
{
    /// <summary>The login attempt's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>When the ceremony started — the moment credentials were presented.</summary>
    public required DateTime AttemptedAt { get; init; }

    /// <summary>Whether the ceremony ended with the user signed in.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Why it failed, when it did. Null while a ceremony is still open.</summary>
    public required string? FailureReason { get; init; }

    /// <summary>The address the ceremony was started from.</summary>
    public required string? IpAddress { get; init; }

    /// <summary>The raw agent string, parsed into a device label on read.</summary>
    public required string? UserAgent { get; init; }

    /// <summary>The challenge this ceremony required, or null if no second factor was involved.</summary>
    public required Guid? TwoFactorChallengeId { get; init; }

    /// <summary>
    /// How many verification codes were rejected during this ceremony. Zero when
    /// no second factor was involved, and also when the challenge row is gone —
    /// the join tolerates its absence rather than dropping the entry.
    /// </summary>
    public required int SecondFactorAttempts { get; init; }

    /// <summary>
    /// True while the ceremony has no outcome: a second factor was demanded and
    /// nothing settled it. Past the challenge lifetime this means the code was
    /// never supplied — somebody produced the password and stopped there.
    /// </summary>
    public bool IsAwaitingSecondFactor =>
        TwoFactorChallengeId.HasValue && !IsSuccess && FailureReason is null;
}
