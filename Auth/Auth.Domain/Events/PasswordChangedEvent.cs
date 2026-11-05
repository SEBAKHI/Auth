using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user's existing password is replaced.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name so the notification handler does not have to load
/// the user again, matching <see cref="UserLoggedInEvent"/> and the session events.
/// </remarks>
public record PasswordChangedEvent(
    Guid UserId,
    Guid ChangedBy,
    string Email,
    string DisplayName) : IDomainEvent
{
    /// <summary>
    /// The account's passkeys that stay after the change (S20, P7-R12): every live one
    /// registered more than 72 hours before it, with when it was added and last used, so the
    /// notice lets the owner spot one they do not recognise. Those registered later were
    /// revoked by the change itself (D-T19 D). A copy, taken when the event is made.
    /// </summary>
    public IReadOnlyList<PasskeySummary> Passkeys { get; init; } = [];
}
