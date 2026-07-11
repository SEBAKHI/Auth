using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetPlatformSettings;

/// <summary>
/// Handler for the admin platform settings query.
/// </summary>
public class GetPlatformSettingsQueryHandler : IRequestHandler<GetPlatformSettingsQuery, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetPlatformSettingsQueryHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(GetPlatformSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return new PlatformSettingsDto
        {
            PlatformName = settings.PlatformName,
            LogoUrl = _imageUrlComposer.Compose(settings.LogoUrl),
            ModifiedAt = settings.ModifiedAt,
            ModifiedBy = settings.ModifiedBy,
            ModifiedByName = settings.ModifiedBy.HasValue
                ? modifierNames.GetValueOrDefault(settings.ModifiedBy.Value)
                : null
        };
    }
}
