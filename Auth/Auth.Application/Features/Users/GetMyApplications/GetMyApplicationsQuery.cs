using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetMyApplications;

/// <summary>
/// Lists the applications the calling user can open from IAM's Home (UI batch item 12).
/// Self-service: the endpoint takes the user from the access token, never from the request.
/// </summary>
public record GetMyApplicationsQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<MyApplicationDto>>>;
