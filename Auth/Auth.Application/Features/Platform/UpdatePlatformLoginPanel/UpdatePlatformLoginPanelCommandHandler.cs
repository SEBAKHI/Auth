using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformLoginPanel;

/// <summary>
/// Handler for showing or hiding the panel beside the sign-in form. Saving the choice already
/// stored writes nothing and raises no event. The answer is always what is stored.
/// </summary>
public class UpdatePlatformLoginPanelCommandHandler
    : IRequestHandler<UpdatePlatformLoginPanelCommand, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdatePlatformLoginPanelCommandHandler> _logger;

    public UpdatePlatformLoginPanelCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdatePlatformLoginPanelCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(
        UpdatePlatformLoginPanelCommand request,
        CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        if (settings.SetLoginPanelShown(request.Shown, request.UpdatedBy))
        {
            await _platformSettingsRepository.UpdateLoginPanelShownAsync(settings, cancellationToken);

            _logger.LogInformation(
                "Sign-in panel shown set to {LoginPanelShown} by {UpdatedBy}",
                settings.LoginPanelShown, request.UpdatedBy);

            await _publisher.Publish(
                new PlatformLoginPanelChangedEvent(
                    Guid.NewGuid(),
                    settings.Id,
                    settings.LoginPanelShown,
                    request.UpdatedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);
        }

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return PlatformSettingsDto.From(settings, _imageUrlComposer, modifierNames);
    }
}
