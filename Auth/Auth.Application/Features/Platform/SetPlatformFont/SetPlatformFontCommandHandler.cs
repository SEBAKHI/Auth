using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.SetPlatformFont;

/// <summary>
/// Handler for setting or removing one of the platform's fonts. The file is checked and
/// stored first, then recorded; a file no role uses any more is deleted, so a replaced font
/// is not served forever. Saving the font a role already has writes nothing and raises no
/// event. The answer is always what is stored.
/// </summary>
public class SetPlatformFontCommandHandler
    : IRequestHandler<SetPlatformFontCommand, ErrorOr<PlatformSettingsDto>>
{
    /// <summary>
    /// How old an unrecorded font file must be before a later save deletes it: far longer than
    /// one save takes, so a save still running beside this one keeps its file.
    /// </summary>
    internal static readonly TimeSpan OrphanAge = TimeSpan.FromHours(1);

    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFontStorageService _fontStorage;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<SetPlatformFontCommandHandler> _logger;

    public SetPlatformFontCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IFontStorageService fontStorage,
        IImageUrlComposer imageUrlComposer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<SetPlatformFontCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _fontStorage = fontStorage;
        _imageUrlComposer = imageUrlComposer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(
        SetPlatformFontCommand request,
        CancellationToken cancellationToken)
    {
        PlatformFont? font = null;
        if (request.Upload is { } upload)
        {
            var size = PlatformFont.CheckFileSize(upload.LengthBytes);
            if (size.IsError)
            {
                return size.Errors;
            }

            var stored = await _fontStorage.SaveAsync(upload.Content, cancellationToken);
            if (stored.IsError)
            {
                return stored.Errors;
            }

            font = PlatformFont.Create(
                stored.Value.Key,
                stored.Value.FamilyName ?? Path.GetFileNameWithoutExtension(upload.FileName));
        }

        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();
        var before = settings.FontFor(request.Role);
        var fileWasInUse = font is not null && settings.UsesFontFile(font.Key);

        if (settings.SetFont(request.Role, font, request.UpdatedBy))
        {
            try
            {
                await _platformSettingsRepository.UpdateFontsAsync(settings, cancellationToken);
            }
            catch when (font is not null && !fileWasInUse)
            {
                // Nothing records the file just written: take it back, so a failed save leaves
                // no file that the next save's sweep would have to find.
                await _fontStorage.DeleteAsync(font.Key, CancellationToken.None);
                throw;
            }

            _logger.LogInformation(
                "Platform {Role} font set to {FamilyName} ({Key}) by {UpdatedBy}",
                request.Role, font?.FamilyName, font?.Key, request.UpdatedBy);

            await _publisher.Publish(
                new PlatformFontChangedEvent(
                    Guid.NewGuid(),
                    settings.Id,
                    request.Role,
                    before?.FamilyName,
                    font?.FamilyName,
                    before?.Key,
                    font?.Key,
                    request.UpdatedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);

            if (before is not null && !settings.UsesFontFile(before.Key))
            {
                await _fontStorage.DeleteAsync(before.Key, CancellationToken.None);
            }
        }

        await _fontStorage.PruneAsync(
            settings.Fonts.Values.Select(stored => stored.Key).ToArray(),
            _time.GetUtcNow().UtcDateTime - OrphanAge,
            CancellationToken.None);

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return PlatformSettingsDto.From(settings, _imageUrlComposer, modifierNames);
    }
}
