using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a sign-in succeeds from a device this user has not been seen on
/// before — and not on their very first sign-in, which would be reporting the
/// action they are performing back to them.
/// </summary>
/// <param name="DeviceName">Human label such as "Chrome on Windows", or null when unrecognised.</param>
/// <param name="IpAddress">The address the sign-in came from, or null when unavailable.</param>
/// <param name="SignedInAtUtc">When it happened.</param>
public record NewDeviceSignInEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    string? DeviceName,
    string? IpAddress,
    DateTime SignedInAtUtc) : IDomainEvent;
