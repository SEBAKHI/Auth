using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// Resumes a pending authorize request that was stored when the browser was
/// sent to an accounts page (OI-145). The accounts page carries only the client
/// id and the handle; the request itself runs again from the stored values,
/// through the same checks as a fresh authorize request.
/// </summary>
/// <param name="ClientId">The <c>client_id</c> the accounts page carried; must be the stored request's.</param>
/// <param name="Handle">The <c>auth_request</c> handle: 64 lowercase hex characters.</param>
/// <param name="SkipOrganization">
/// <c>skip_organization=true</c>: the user chose to continue without an
/// organization, so the stored request stops asking for one, for good.
/// </param>
/// <param name="IdpSessionToken">The plain IdP session cookie value, if present.</param>
/// <param name="StepUpTicket">The step-up cookie value, if present.</param>
/// <param name="IpAddress">The client's IP address.</param>
/// <param name="UserAgent">The browser's <c>User-Agent</c> header (OI-178).</param>
public record ResumeAuthorizationCommand(
    string? ClientId,
    string? Handle,
    bool SkipOrganization,
    string? IdpSessionToken,
    string? StepUpTicket,
    string? IpAddress,
    string? UserAgent = null) : IRequest<ErrorOr<AuthorizeResult>>;
