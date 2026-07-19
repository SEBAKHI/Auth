using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetPublicBranding;

/// <summary>
/// Anonymous query for the minimal public branding of an application, shown on
/// the hosted login page during an authorize flow. Exposes nothing beyond the
/// display name and logo.
/// </summary>
/// <param name="ClientId">The application code acting as the public client id.</param>
public record GetPublicBrandingQuery(string ClientId) : IRequest<ErrorOr<PublicBrandingDto>>;

/// <summary>
/// Minimal public-facing application branding.
/// </summary>
public record PublicBrandingDto
{
    public required string Name { get; init; }
    public string? LogoUrl { get; init; }
}
