using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.StartRegistration;

/// <summary>
/// The first step of a verify-first self-registration: an address, and nothing
/// else. No account, no password, no name exists until the code this step mails
/// comes back through the completion step.
/// </summary>
/// <param name="Email">The address the caller typed.</param>
/// <param name="PreferredLanguage">The site language, if the client states one; the request culture otherwise.</param>
/// <param name="IpAddress">The caller's address as the gateway forwarded it, for the audit trail.</param>
/// <param name="UserAgent">The caller's user agent, for the audit trail.</param>
public record StartRegistrationCommand(
    string Email,
    string? PreferredLanguage,
    string? IpAddress,
    string? UserAgent) : IRequest<ErrorOr<StartRegistrationResponse>>;

/// <summary>
/// The same shape for every address. Nothing in it says whether the address was
/// free, taken, or reserved: the only difference between those is which message
/// went out, and to whom.
/// </summary>
/// <param name="PendingId">The opaque handle the next two steps present. Not a secret; the code is the proof.</param>
/// <param name="MaskedEmail">The address as the screen shows it, derived from the normalized input.</param>
/// <param name="ExpiresAt">When the pending row's current code dies — the stored value, not a nominal one.</param>
public record StartRegistrationResponse(string PendingId, string MaskedEmail, DateTime ExpiresAt);
