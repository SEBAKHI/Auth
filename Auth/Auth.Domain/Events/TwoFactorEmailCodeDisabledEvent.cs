using Auth.Domain.Constants;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the emailed code stopped being the account's second factor (S24): the
/// owner switched it off, or an authenticator app took its place.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, so the notice that tells the owner does not have to load
/// the user again — the pattern of <see cref="TwoFactorDisabledEvent"/>.
/// <para>
/// <paramref name="Reason"/> is one of <see cref="TwoFactorEmailCodeDisableReasons"/>.
/// When an authenticator app took its place, a <see cref="TwoFactorEnabledEvent"/> is
/// raised just after this one, and the owner is told once, by that event's notice.
/// </para>
/// </remarks>
public record TwoFactorEmailCodeDisabledEvent(
    Guid UserId,
    Guid DisabledBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    string Reason) : IDomainEvent;
