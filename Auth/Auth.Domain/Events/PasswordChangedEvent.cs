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
    /// The account's passkeys that stay after the change (S20, P7-R12): every one still live,
    /// read after the change's 72-hour revocation ran (D-T19 D), with when it was added and
    /// last used, so the notice lets the owner spot one they do not recognise. When that
    /// revocation failed the recent ones are listed too: the notice never hides a passkey that
    /// still signs in (REVIEW 150 F2). A copy, taken when the event is made.
    /// </summary>
    public IReadOnlyList<PasskeySummary> Passkeys { get; init; } = [];
}
