using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetPlatformBranding;

/// <summary>
/// Handler for the public platform branding query. Falls back to the default
/// branding when the settings row has not been seeded yet.
/// </summary>
public class GetPlatformBrandingQueryHandler : IRequestHandler<GetPlatformBrandingQuery, ErrorOr<PlatformBrandingDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetPlatformBrandingQueryHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<PlatformBrandingDto>> Handle(GetPlatformBrandingQuery request, CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        return new PlatformBrandingDto
        {
            PlatformName = settings.PlatformName,
            LogoUrl = _imageUrlComposer.Compose(settings.LogoUrl),
            LogoUrlDark = _imageUrlComposer.Compose(settings.LogoUrlDark),
            FaviconUrl = _imageUrlComposer.Compose(settings.FaviconUrl)
        };
    }
}
