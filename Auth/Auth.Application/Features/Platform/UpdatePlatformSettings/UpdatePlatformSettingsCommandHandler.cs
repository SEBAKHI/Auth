using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformSettings;

/// <summary>
/// Handler for updating the platform branding settings.
/// </summary>
public class UpdatePlatformSettingsCommandHandler : IRequestHandler<UpdatePlatformSettingsCommand, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly ILogger<UpdatePlatformSettingsCommandHandler> _logger;

    public UpdatePlatformSettingsCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        ILogger<UpdatePlatformSettingsCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(UpdatePlatformSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        var oldPlatformName = settings.PlatformName;
        var oldLogoUrl = settings.LogoUrl;

        settings.Update(request.PlatformName, request.LogoUrl, request.UpdatedBy);
        await _platformSettingsRepository.UpdateAsync(settings, cancellationToken);

        await _publisher.Publish(
            new PlatformSettingsUpdatedEvent(
                settings.Id,
                oldPlatformName,
                settings.PlatformName,
                oldLogoUrl,
                settings.LogoUrl,
                request.UpdatedBy),
            cancellationToken);

        _logger.LogInformation(
            "Platform settings updated by {UpdatedBy}: name '{OldName}' -> '{NewName}'",
            request.UpdatedBy, oldPlatformName, settings.PlatformName);

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
