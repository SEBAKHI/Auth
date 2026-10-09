using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformTheme;

/// <summary>
/// Handler for replacing the platform appearance.
/// </summary>
public class UpdatePlatformThemeCommandHandler : IRequestHandler<UpdatePlatformThemeCommand, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly ILogger<UpdatePlatformThemeCommandHandler> _logger;

    public UpdatePlatformThemeCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        ILogger<UpdatePlatformThemeCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(UpdatePlatformThemeCommand request, CancellationToken cancellationToken)
    {
        var theme = PlatformTheme.Create(
            request.BaseColor, request.Theme, request.Chart, request.Radius, request.MenuAccent);
        if (theme.IsError)
        {
            return theme.Errors;
        }

        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();
        var oldTheme = settings.Theme;

        settings.UpdateTheme(theme.Value, request.UpdatedBy);
        await _platformSettingsRepository.UpdateThemeAsync(settings, cancellationToken);

        await _publisher.Publish(
            new PlatformThemeUpdatedEvent(settings.Id, oldTheme, settings.Theme, request.UpdatedBy),
            cancellationToken);

        _logger.LogInformation("Platform appearance updated by {UpdatedBy}", request.UpdatedBy);

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return new PlatformSettingsDto
        {
            PlatformName = settings.PlatformName,
            LogoUrl = _imageUrlComposer.Compose(settings.LogoUrl),
            LogoUrlDark = _imageUrlComposer.Compose(settings.LogoUrlDark),
            FaviconUrl = _imageUrlComposer.Compose(settings.FaviconUrl),
            Theme = PlatformThemeDto.From(settings.Theme),
            ModifiedAt = settings.ModifiedAt,
            ModifiedBy = settings.ModifiedBy,
            ModifiedByName = settings.ModifiedBy.HasValue
                ? modifierNames.GetValueOrDefault(settings.ModifiedBy.Value)
                : null
        };
    }
}
