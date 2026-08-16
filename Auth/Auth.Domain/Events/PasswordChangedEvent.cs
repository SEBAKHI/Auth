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
    string DisplayName) : IDomainEvent;
