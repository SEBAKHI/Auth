using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an account acquires its FIRST local password.
/// </summary>
/// <remarks>
/// Deliberately distinct from <see cref="PasswordChangedEvent"/>: an external-only account
/// (Google, Apple) gaining a credential it never had is a different security event from
/// rotating one that already existed, and the audit log has to tell the two apart.
/// </remarks>
public record PasswordCreatedEvent(
    Guid UserId,
    Guid SetBy,
    string Email,
    string DisplayName) : IDomainEvent;
