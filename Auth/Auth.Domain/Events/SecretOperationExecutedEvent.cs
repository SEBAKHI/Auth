using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// A destructive secret operation ran to completion. Until this event existed,
/// rotating the key that signs every access token in the platform left no trace
/// anywhere but an application log line — nothing in AuditLogs, nothing an
/// incident responder could query.
/// </summary>
/// <param name="ChallengeId">The confirmation that was spent on it.</param>
/// <param name="Operation">The operation that ran.</param>
/// <param name="ExecutedBy">The administrator who ran it.</param>
public record SecretOperationExecutedEvent(
    Guid ChallengeId,
    SecretOperation Operation,
    Guid ExecutedBy) : IDomainEvent;
