using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformLogoHeight;

/// <summary>
/// Handler for setting the platform logo's height on the sign-in pages. The range is the
/// domain's (<see cref="LogoHeight.CreateOptional"/>); saving the height already stored writes
/// nothing and raises no event. The answer is always what is stored.
/// </summary>
public class UpdatePlatformLogoHeightCommandHandler
    : IRequestHandler<UpdatePlatformLogoHeightCommand, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdatePlatformLogoHeightCommandHandler> _logger;

    public UpdatePlatformLogoHeightCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdatePlatformLogoHeightCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(
        UpdatePlatformLogoHeightCommand request,
        CancellationToken cancellationToken)
    {
        var height = LogoHeight.CreateOptional(request.LogoHeightPx);
        if (height.IsError)
        {
            return height.Errors;
        }

        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();
        var before = settings.LogoHeight?.Px;

        if (settings.SetLogoHeight(height.Value, request.UpdatedBy))
        {
            await _platformSettingsRepository.UpdateLogoHeightAsync(settings, cancellationToken);

            _logger.LogInformation(
                "Platform logo height set to {LogoHeightPx} by {UpdatedBy}",
                settings.LogoHeight?.Px, request.UpdatedBy);

            await _publisher.Publish(
                new PlatformLogoHeightChangedEvent(
                    Guid.NewGuid(),
                    settings.Id,
                    before,
                    settings.LogoHeight?.Px,
                    request.UpdatedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);
        }

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return PlatformSettingsDto.From(settings, _imageUrlComposer, modifierNames);
    }
}
