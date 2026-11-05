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
    string DisplayName) : IDomainEvent
{
    /// <summary>
    /// The account's passkeys that stay once the password is set (S20, REVIEW 150 F12): a
    /// password set by the reset link revokes those registered in the 72 hours before it
    /// (D-T19 D), and the notice lists the others, as the password-changed notice does, so
    /// the owner can spot one they do not recognise. A copy, taken when the event is made.
    /// </summary>
    public IReadOnlyList<PasskeySummary> Passkeys { get; init; } = [];
}
