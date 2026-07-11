using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetPlatformBranding;

/// <summary>
/// Query for the public platform branding (name + logo). Served anonymously.
/// </summary>
public record GetPlatformBrandingQuery() : IRequest<ErrorOr<PlatformBrandingDto>>;
