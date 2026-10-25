using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when two-factor authentication is disabled for a user.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, so the notice that tells the owner does not have to load
/// the user again — the pattern of <see cref="PasswordChangedEvent"/>.
/// <para>
/// <paramref name="ReplacedByEmailCode"/> is true when the authenticator app was
/// switched off because the emailed code took its place (S24): a
/// <see cref="TwoFactorEmailCodeEnabledEvent"/> follows, and its notice is the one
/// the owner receives.
/// </para>
/// </remarks>
public record TwoFactorDisabledEvent(
    Guid UserId,
    Guid DisabledBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    bool ReplacedByEmailCode = false) : IDomainEvent;
