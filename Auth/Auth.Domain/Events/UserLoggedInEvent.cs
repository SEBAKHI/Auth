using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user successfully logs in.
/// </summary>
public record UserLoggedInEvent(
    Guid UserId,
    string Email,
    string? IpAddress,
    string? UserAgent) : IDomainEvent;
