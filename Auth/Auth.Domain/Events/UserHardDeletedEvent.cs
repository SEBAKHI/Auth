using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a soft-deleted user account is permanently removed together
/// with all of its dependent records (sessions, tokens, memberships, audit
/// trail). The event itself is the only remaining trace of the account.
/// </summary>
public record UserHardDeletedEvent(
    Guid UserId,
    string Email,
    Guid DeletedBy) : IDomainEvent;
