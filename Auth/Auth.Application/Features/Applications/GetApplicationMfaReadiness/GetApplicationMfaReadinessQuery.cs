using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationMfaReadiness;

/// <summary>
/// How ready an application's recent users are for its two-step requirement (S23, AM11).
/// </summary>
/// <param name="ApplicationId">The application.</param>
public record GetApplicationMfaReadinessQuery(Guid ApplicationId) : IRequest<ErrorOr<ApplicationMfaReadinessDto>>;
