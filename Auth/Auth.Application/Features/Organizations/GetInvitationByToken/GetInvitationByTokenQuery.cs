using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetInvitationByToken;

/// <summary>
/// Query to preview an organization invitation by its token.
/// Public: possession of the emailed single-use token is the authorization.
/// </summary>
public record GetInvitationByTokenQuery(string Token) : IRequest<ErrorOr<InvitationPreviewDto>>;
