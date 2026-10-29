using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetPublicBranding;

/// <summary>
/// Handles the public branding lookup. Unknown and inactive applications are
/// indistinguishable (both 404) so the anonymous endpoint cannot be used to
/// probe the application catalog.
/// </summary>
public class GetPublicBrandingQueryHandler
    : IRequestHandler<GetPublicBrandingQuery, ErrorOr<PublicBrandingDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetPublicBrandingQueryHandler(
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _applicationRepository = applicationRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<PublicBrandingDto>> Handle(
        GetPublicBrandingQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return ApplicationErrors.NotFoundByCode(request.ClientId ?? string.Empty);
        }

        var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
        if (application is null || !application.IsActive)
        {
            return ApplicationErrors.NotFoundByCode(request.ClientId);
        }

        // An uploaded logo is stored as a storage key; the sign-in pages live on
        // another origin, so they need the composed absolute URL.
        return new PublicBrandingDto
        {
            Name = application.Name,
            LogoUrl = _imageUrlComposer.Compose(application.LogoUrl),
            LogoUrlDark = _imageUrlComposer.Compose(application.LogoUrlDark),
            // Only for an application a browser can sign in to: that is the page that
            // offers the way home. A machine client never exposes its address on this
            // anonymous endpoint. The rule is shared with the launch after an accepted
            // invitation (OI-192), so the two can never disagree about it.
            HomeUrl = ApplicationLaunchTarget.HomeUrlOf(application),
            // The whole object or nothing: the page applies it in place of the platform's, never
            // merged with it, and an application whose switch is off shows the platform's.
            Theme = PlatformThemeDto.FromOrNull(application.EffectiveTheme)
        };
    }
}
