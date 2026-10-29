using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.GetPublicLoginDesign;

/// <summary>
/// Anonymous: the design a sign-in page shows in its panel, rendered for one language. For an
/// application, its active design, else the platform's; for the platform's own pages, the
/// platform's. An unknown or inactive application answers as missing, as its public branding does.
/// </summary>
/// <param name="ClientId">The application's code (<c>client_id</c>); null for the platform's own pages.</param>
/// <param name="Language">The page's language; anything unsupported renders the default language.</param>
public sealed record GetPublicLoginDesignQuery(string? ClientId, string? Language) : IRequest<ErrorOr<PublicLoginDesignDto>>;
