using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the emailed code became the account's second factor (S24): after the
/// password, sign-in asks for a code sent to the account's address.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, so the notice that tells the owner does not have to load
/// the user again — the pattern of <see cref="TwoFactorEnabledEvent"/>.
/// <para>
/// <paramref name="ReplacedAuthenticator"/> is true when the emailed code took the
/// place of an authenticator app (the two are never bound together): a
/// <see cref="TwoFactorDisabledEvent"/> for the app is raised just before this one,
/// and the owner is told once, by this event's notice.
/// </para>
/// </remarks>
public record TwoFactorEmailCodeEnabledEvent(
    Guid UserId,
    Guid EnabledBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    bool ReplacedAuthenticator) : IDomainEvent;
