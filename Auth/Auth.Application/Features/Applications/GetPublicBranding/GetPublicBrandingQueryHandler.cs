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
            LogoUrlDark = _imageUrlComposer.Compose(application.LogoUrlDark)
        };
    }
}
