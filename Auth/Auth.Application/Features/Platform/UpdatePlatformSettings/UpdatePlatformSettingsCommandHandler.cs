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
    private readonly IImageStorageService _imageStorage;
    private readonly IPublisher _publisher;
    private readonly ILogger<UpdatePlatformSettingsCommandHandler> _logger;

    public UpdatePlatformSettingsCommandHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        IImageStorageService imageStorage,
        IPublisher publisher,
        ILogger<UpdatePlatformSettingsCommandHandler> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _imageStorage = imageStorage;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<PlatformSettingsDto>> Handle(UpdatePlatformSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        var oldPlatformName = settings.PlatformName;
        var oldLogoUrl = settings.LogoUrl;
        var oldLogoUrlDark = settings.LogoUrlDark;
        var oldFaviconUrl = settings.FaviconUrl;

        // Clients resend the composed absolute URL they last read; store the
        // raw key so replaced-file cleanup and future URL changes stay sound.
        settings.Update(
            request.PlatformName,
            _imageUrlComposer.Decompose(request.LogoUrl),
            _imageUrlComposer.Decompose(request.LogoUrlDark),
            _imageUrlComposer.Decompose(request.FaviconUrl),
            request.UpdatedBy);
        await _platformSettingsRepository.UpdateAsync(settings, cancellationToken);

        await _publisher.Publish(
            new PlatformSettingsUpdatedEvent(
                settings.Id,
                oldPlatformName,
                settings.PlatformName,
                oldLogoUrl,
                settings.LogoUrl,
                oldLogoUrlDark,
                settings.LogoUrlDark,
                oldFaviconUrl,
                settings.FaviconUrl,
                request.UpdatedBy),
            cancellationToken);

        await DeleteReplacedLogoFilesAsync(
            oldValues: [oldLogoUrl, oldLogoUrlDark, oldFaviconUrl],
            newValues: [settings.LogoUrl, settings.LogoUrlDark, settings.FaviconUrl],
            cancellationToken);

        // Email cannot use the stored WebP, so each logo gets an opaque PNG rendition built
        // here — at admin time, on a thread that can afford it — rather than on the send path.
        await EnsureEmailLogoRenditionsAsync(settings, cancellationToken);

        _logger.LogInformation(
            "Platform settings updated by {UpdatedBy}: name '{OldName}' -> '{NewName}'",
            request.UpdatedBy, oldPlatformName, settings.PlatformName);

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [settings.ModifiedBy], cancellationToken);

        return new PlatformSettingsDto
        {
            PlatformName = settings.PlatformName,
            LogoUrl = _imageUrlComposer.Compose(settings.LogoUrl),
            LogoUrlDark = _imageUrlComposer.Compose(settings.LogoUrlDark),
            FaviconUrl = _imageUrlComposer.Compose(settings.FaviconUrl),
            ModifiedAt = settings.ModifiedAt,
            ModifiedBy = settings.ModifiedBy,
            ModifiedByName = settings.ModifiedBy.HasValue
                ? modifierNames.GetValueOrDefault(settings.ModifiedBy.Value)
                : null
        };
    }

    /// <summary>
    /// Rebuilds the email-safe PNG renditions for whichever logo slots are set. Best-effort:
    /// a storage fault must not fail an otherwise successful settings update — emails fall
    /// back to the text wordmark, which stays legible on both surfaces.
    /// </summary>
    private async Task EnsureEmailLogoRenditionsAsync(
        PlatformSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await _imageStorage.EnsureEmailLogoRenditionAsync(
                settings.LogoUrl, EmailLogoVariant.Light, cancellationToken);
            await _imageStorage.EnsureEmailLogoRenditionAsync(
                settings.LogoUrlDark, EmailLogoVariant.Dark, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Email logo renditions could not be rebuilt after a platform settings update. " +
                "Emails will show the platform name as a text wordmark until this succeeds.");
        }
    }

    /// <summary>
    /// Best-effort removal of logo files no longer referenced by either slot.
    /// Rows written before key normalization may hold composed absolute URLs,
    /// so still-referenced files are matched by file name and old values are
    /// decomposed back to keys before deletion (external URLs stay no-ops).
    /// </summary>
    private async Task DeleteReplacedLogoFilesAsync(
        string?[] oldValues,
        string?[] newValues,
        CancellationToken cancellationToken)
    {
        var retainedFileNames = newValues
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => Path.GetFileName(value!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var oldValue in oldValues.Where(value => !string.IsNullOrEmpty(value)).Distinct())
        {
            if (!retainedFileNames.Contains(Path.GetFileName(oldValue!)))
            {
                await _imageStorage.DeleteImageAsync(
                    _imageUrlComposer.Decompose(oldValue), cancellationToken);
            }
        }
    }
}
