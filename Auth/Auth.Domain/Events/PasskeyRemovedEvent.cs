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
/// the owner (<see cref="Constants.PasskeyRemovalReasons.User"/>), and for each passkey the
/// system revoked by itself — a password change, a password reset (a first password set by
/// one included), an administrator's lock — in words that name the passkey, say why and say
/// what to do (REVIEW 150 F12). An administrator's two-factor reset sends its own notice and
/// none per passkey. Carries no credential id, public key or user handle (PK16).
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="PasskeyId">The passkey's row id.</param>
/// <param name="Name">The name its owner gave it.</param>
/// <param name="AddedAtUtc">When it was added (UTC), so its notice can say which one it was.</param>
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
    DateTime AddedAtUtc,
    string Reason,
    Guid? RemovedBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    DateTime OccurredAtUtc) : IDomainEvent;
