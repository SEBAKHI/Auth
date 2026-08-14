using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// A stored secret value was replaced through the admin API outside the
/// challenge-gated rotation flow — currently the database connection string and
/// the SMTP password.
/// </summary>
/// <remarks>
/// These operations deliberately carry no step-up confirmation (the code would be
/// delivered over the very channel being repaired), so this event is the only
/// durable record that they happened. Repointing the API at a different database
/// is among the most consequential changes an administrator can make; it must not
/// be reconstructable only from application logs.
/// <para>
/// <paramref name="SecretKey"/> is the storage key name, never the value and
/// never a digest of it: a digest of a connection string is an offline guessing
/// target, and the audit trail answers "what was changed", not "to what".
/// </para>
/// </remarks>
/// <param name="SecretKey">Storage key that was written, e.g. <c>ConnectionStrings.AuthDb</c>.</param>
/// <param name="ChangedBy">The administrator who changed it.</param>
public record SecretValueChangedEvent(
    string SecretKey,
    Guid ChangedBy) : IDomainEvent;
