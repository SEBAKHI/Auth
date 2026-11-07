using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user successfully logs in.
/// </summary>
/// <param name="UserId">The account that signed in.</param>
/// <param name="Email">The account's address.</param>
/// <param name="IpAddress">The client's address.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="Methods">
/// What the sign-in proved (S20, R-EVT): the same methods the exit gives the session it
/// mints. <see cref="AuthenticationMethods.Unknown"/> when a raiser names none.
/// </param>
public record UserLoggedInEvent(
    Guid UserId,
    string Email,
    string? IpAddress,
    string? UserAgent,
    AuthenticationMethods Methods = default) : IDomainEvent;
