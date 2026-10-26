using Auth.Domain.Constants;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an account's password was removed (OI-215): from then on it signs in
/// with a code emailed to its address.
/// </summary>
/// <remarks>
/// Distinct from <see cref="PasswordChangedEvent"/> and <see cref="PasswordCreatedEvent"/>:
/// an account losing its password is a credential change of its own, and the audit log
/// and the owner's notice have to tell it apart. Carries the recipient's address and
/// name so the notice does not load the user again.
/// <para>
/// <paramref name="Reason"/> is one of <see cref="PasswordRemovalReasons"/>.
/// <paramref name="RemovedBy"/> is the account itself in both cases: the owner removed
/// it, or the owner's sign-in by code confirmed the address and retired it.
/// </para>
/// </remarks>
public record PasswordRemovedEvent(
    Guid UserId,
    Guid RemovedBy,
    string Email,
    string DisplayName,
    string Reason) : IDomainEvent;
