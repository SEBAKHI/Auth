using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised after a passkey stopped working and the change was saved (S20, PK12): its owner
/// removed it, an administrator reset the account's second factor, or a password reset, a
/// password change or an administrator's lock revoked the passkeys registered in the 72
/// hours before it (D-T19 D). One event per passkey.
/// </summary>
/// <remarks>
/// The audit handler records every one. The owner's notice (FA11) is sent for a removal by
/// the owner (<see cref="Constants.PasskeyRemovalReasons.User"/>): the other reasons come
/// with a notice of their own (the administrator's reset, the password change, the lock),
/// so a second email per passkey would only bury it. Carries no credential id, public key
/// or user handle (PK16).
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="PasskeyId">The passkey's row id.</param>
/// <param name="Name">The name its owner gave it.</param>
/// <param name="Reason">Why: a value of <see cref="Constants.PasskeyRemovalReasons"/>.</param>
/// <param name="RemovedBy">The person who removed it: the owner, or the administrator.</param>
/// <param name="Email">The account's address, for the notice.</param>
/// <param name="DisplayName">The account's display name, for the notice.</param>
/// <param name="DeviceName">The browser and operating system of the owner's session, when the owner removed it.</param>
/// <param name="OccurredAtUtc">When it stopped working (UTC).</param>
public sealed record PasskeyRemovedEvent(
    Guid UserId,
    Guid PasskeyId,
    string Name,
    string Reason,
    Guid? RemovedBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    DateTime OccurredAtUtc) : IDomainEvent;
