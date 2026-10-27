using Auth.Application.Features.Authentication.StartRegistration;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.StartEmailCodeSignIn;

/// <summary>
/// The first step of a sign-in by emailed code (OI-215), which is also the first step
/// of a sign-up: an address, and nothing else. A code goes to the address when it has
/// an account or may get one; the answer is the same for every address.
/// </summary>
/// <param name="Email">The address the visitor typed.</param>
/// <param name="PreferredLanguage">The site language, if the client states one; the request culture otherwise.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it, for the audit trail.</param>
/// <param name="UserAgent">The caller's user agent, for the audit trail.</param>
public record StartEmailCodeSignInCommand(
    string Email,
    string? PreferredLanguage,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<StartRegistrationResponse>>;
