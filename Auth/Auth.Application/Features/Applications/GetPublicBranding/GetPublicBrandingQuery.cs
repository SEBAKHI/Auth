using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetPublicBranding;

/// <summary>
/// Anonymous query for the minimal public branding of an application, shown on
/// the hosted login page during an authorize flow. Exposes nothing beyond the
/// display name, the logos and, for an application a browser can sign in to, its
/// home address.
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

    /// <summary>
    /// The dark-mode logo, or null when the application has one logo for both
    /// modes (clients fall back to <see cref="LogoUrl"/>).
    /// </summary>
    public string? LogoUrlDark { get; init; }

    /// <summary>
    /// Where the application is hosted (<c>Application.BaseUrl</c>), so a page that
    /// cannot send the visitor back through a sign-in request can still offer the
    /// way home (OI-145). Null unless the application has at least one registered
    /// redirect URI (one a browser can sign in to; D-68-1 (c)), and unless it is an
    /// absolute <c>https</c> URL, or <c>http</c> on a loopback host: the field is
    /// checked for length alone when it is saved, and a page renders it as a link.
    /// </summary>
    public string? HomeUrl { get; init; }
}
