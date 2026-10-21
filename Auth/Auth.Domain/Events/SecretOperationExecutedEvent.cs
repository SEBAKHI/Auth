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
/// <param name="Action">
/// For a change to the HMAC keyring (S11), which one: one operation confirms several
/// keyring actions, and the trail must say which ran. Null for every other operation.
/// </param>
/// <param name="Target">The keyring key the action applied to, when it names one.</param>
public record SecretOperationExecutedEvent(
    Guid ChallengeId,
    SecretOperation Operation,
    Guid ExecutedBy,
    KeyringAction? Action = null,
    int? Target = null) : IDomainEvent;
