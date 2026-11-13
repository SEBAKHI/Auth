using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RequestPasswordSetupLink;

/// <summary>
/// A signed-in person asks for a link, mailed to their own address, to set a password (S13, D7):
/// the set-password panel of the profile and of the forced password change, which used to call the
/// anonymous forgot-password request with the address. The address is the session's account's,
/// never one the request names.
/// </summary>
/// <param name="UserId">The caller's own account (the token's subject).</param>
public sealed record RequestPasswordSetupLinkCommand(Guid UserId) : IRequest<ErrorOr<PasswordResetLinkResponse>>;
