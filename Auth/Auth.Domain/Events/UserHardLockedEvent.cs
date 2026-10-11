using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised once an authenticator's consecutive failures reached the hard limit, so
/// the account stays locked until its owner proves the password, resets it, or an
/// administrator acts. Published after the statement that wrote the lock.
/// </summary>
/// <remarks>
/// Unlike most events in this folder it carries the contract fields an event is
/// meant to have: its own id, the factor, the count, who caused it and when.
/// <see cref="TriggeredBy"/> is null because wrong guesses identify nobody.
/// </remarks>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="UserId">The locked account.</param>
/// <param name="Factor">The authenticator whose counter reached the limit.</param>
/// <param name="FailedAttempts">The counter as the locking statement left it.</param>
/// <param name="TriggeredBy">Always null for a lock raised by failures.</param>
/// <param name="OccurredAt">When the lock was written.</param>
public sealed record UserHardLockedEvent(
    Guid EventId,
    Guid UserId,
    HardLockedFactor Factor,
    int FailedAttempts,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
