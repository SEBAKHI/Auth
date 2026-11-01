using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.UpdatePlatformHiddenLanguages;

/// <summary>
/// Handler for setting the languages hidden from the language menus of both apps. The rules are
/// the domain's (<see cref="HiddenLanguages.Create"/>: supported codes only, never English);
/// saving the languages already hidden writes nothing and raises no event. The answer is always
/// what is stored.
/// </summary>
public class UpdatePlatformHiddenLanguagesCommandHandler
    : IRequestHandler<UpdatePlatformHiddenLanguagesCommand, ErrorOr<PlatformSettingsDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdatePlatformHiddenLanguagesCommandHandler> _logger;

    public UpdatePlatformHiddenLanguagesCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdatePlatformHiddenLanguagesCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(
        UpdatePlatformHiddenLanguagesCommand request,
        CancellationToken cancellationToken)
    {
        var hidden = HiddenLanguages.Create(request.HiddenLanguages);
        if (hidden.IsError)
        {
            return hidden.Errors;
        }

        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();
        var before = settings.HiddenLanguages.Codes;

        if (settings.SetHiddenLanguages(hidden.Value, request.UpdatedBy))
        {
            await _platformSettingsRepository.UpdateHiddenLanguagesAsync(settings, cancellationToken);

            _logger.LogInformation(
                "Hidden languages set to {HiddenLanguages} by {UpdatedBy}",
                settings.HiddenLanguages.Codes, request.UpdatedBy);

            await _publisher.Publish(
                new PlatformHiddenLanguagesChangedEvent(
                    Guid.NewGuid(),
                    settings.Id,
                    before,
                    settings.HiddenLanguages.Codes,
                    request.UpdatedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);
        }

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return PlatformSettingsDto.From(settings, _imageUrlComposer, modifierNames);
    }
}
