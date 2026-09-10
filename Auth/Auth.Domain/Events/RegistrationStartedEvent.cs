using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// A self-registration was started for an address: a pending row exists and a
/// message (a code, or a notice to the address's owner) was produced. Raised on
/// every start, whatever the address turned out to be, because under
/// verify-first registration an abandoned or abusive attempt leaves no Users
/// row and therefore no UserCreated row — and those attempts are exactly what
/// gets monitored. The subject is the pending row, never a user: there is none.
/// </summary>
/// <param name="PendingRegistrationId">The pending row the attempt was recorded on.</param>
/// <param name="Email">The typed address, lower-cased. Consumers mask it before storing.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it.</param>
/// <param name="UserAgent">The caller's user agent.</param>
public record RegistrationStartedEvent(
    Guid PendingRegistrationId,
    string Email,
    string? IpAddress,
    string? UserAgent) : IDomainEvent;
