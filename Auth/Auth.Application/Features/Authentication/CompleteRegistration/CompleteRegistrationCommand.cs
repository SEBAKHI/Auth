using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.CompleteRegistration;

/// <summary>
/// The third step of a verify-first self-registration: the code again, and
/// now the password and the name. This is the step that creates the account
/// and consumes the code. There is deliberately no address in it: the handle
/// fixes the address, which is what makes it uneditable on the screen and
/// unforgeable on the wire. No phone number either — it is written on another
/// connection after the account row exists, which cannot see the row inside
/// this step's transaction; it returns as a profile edit after sign-in.
/// </summary>
/// <param name="PendingId">The handle the start step answered with.</param>
/// <param name="Otp">The code that reached the address, presented once more.</param>
/// <param name="Password">The new account's password.</param>
/// <param name="FirstName">The new account's first name.</param>
/// <param name="LastName">The new account's last name.</param>
/// <param name="TimeZone">IANA time zone, or null for UTC.</param>
/// <param name="CreateOrganization">Whether to create the personal organization; off by default.</param>
/// <param name="DeviceId">The calling browser's own identifier, for the session.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it.</param>
/// <param name="UserAgent">The caller's user agent.</param>
public record CompleteRegistrationCommand(
    string PendingId,
    string Otp,
    string Password,
    string FirstName,
    string LastName,
    string? TimeZone,
    bool CreateOrganization,
    string? DeviceId,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<LoginResponse>>;
