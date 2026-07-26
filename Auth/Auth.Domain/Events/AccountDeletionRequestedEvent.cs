using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user requests account deletion: the account is deactivated,
/// all credentials are revoked, and the grace window starts.
/// </summary>
public record AccountDeletionRequestedEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    AccountDeletionSource Source,
    DateTime GraceEndsAtUtc) : IDomainEvent;
