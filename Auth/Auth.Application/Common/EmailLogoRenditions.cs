using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Common;

/// <summary>
/// The one place that decides which email logo backgrounds exist and builds them: one per
/// logo (light, dark) and card colour that an email layout uses or once used.
/// </summary>
/// <remarks>
/// <para>
/// Building happens here, at admin and startup time, and never on the send path, which only
/// looks a rendition up (with <c>Notifications:UseOutbox</c> on, a send runs inside the HTTP
/// request that triggers it, and the uploads directory is not reliably writable there).
/// </para>
/// <para>
/// Best-effort throughout: a storage fault never fails the admin action that triggered it.
/// A layout whose background could not be built shows the platform name as text instead of
/// the logo, which stays legible on every card.
/// </para>
/// </remarks>
public class EmailLogoRenditions
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IImageStorageService _imageStorage;
    private readonly ILogger<EmailLogoRenditions> _logger;

    public EmailLogoRenditions(
        IPlatformSettingsRepository platformSettingsRepository,
        INotificationLayoutRepository layoutRepository,
        IImageStorageService imageStorage,
        ILogger<EmailLogoRenditions> logger)
    {
        _platformSettingsRepository = platformSettingsRepository;
        _layoutRepository = layoutRepository;
        _imageStorage = imageStorage;
        _logger = logger;
    }

    /// <summary>
    /// Builds the backgrounds a layout's colours need and that are not built yet, so the
    /// preview and the first message after publishing both show the logo on its own card.
    /// </summary>
    /// <remarks>
    /// Only missing ones: an existing file already carries the current logo, because a logo
    /// change rebuilds every background on disk (<see cref="RebuildAllAsync"/>). A colour no
    /// message uses yet costs nothing to build, so this is safe ahead of a publish.
    /// </remarks>
    public async Task EnsureAsync(CardColors colours, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _platformSettingsRepository.GetAsync(cancellationToken);
            if (settings is null)
            {
                return;
            }

            foreach (var background in BackgroundsOf(colours))
            {
                var source = SourceKey(settings, background.Variant);
                var existing = await _imageStorage.GetEmailLogoRenditionAsync(
                    source, background.Variant, background.Colour, cancellationToken);
                if (existing is null)
                {
                    await _imageStorage.EnsureEmailLogoRenditionAsync(
                        source, background.Variant, background.Colour, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Email logo backgrounds for card colours {Light} / {Dark} could not be built. " +
                "Emails on that layout show the platform name as text until this succeeds.",
                colours.Light, colours.Dark);
        }
    }

    /// <summary>
    /// Rebuilds every background with the current logos: the shipped defaults, every email
    /// layout's draft and live colours, and every background already on disk. Returns how
    /// many were built, or null when the rebuild could not run.
    /// </summary>
    /// <remarks>
    /// The files on disk are included because mail already sent keeps pointing at the
    /// background of the card it was sent on, even after its layout moved to another colour.
    /// Rebuilding those too is what lets a new logo reach every message ever sent, as it did
    /// when there was one file per logo.
    /// </remarks>
    public async Task<int?> RebuildAllAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var backgrounds = new HashSet<EmailLogoBackground>(BackgroundsOf(CardColors.Default));

            var layouts = await _layoutRepository.GetAllAsync(cancellationToken);
            foreach (var layout in layouts.Where(l => l.Channel == NotificationChannelType.Email))
            {
                backgrounds.UnionWith(BackgroundsOf(layout.DraftCardColors));
                backgrounds.UnionWith(BackgroundsOf(layout.PublishedCardColors));
            }

            backgrounds.UnionWith(await _imageStorage.GetBuiltEmailLogoBackgroundsAsync(cancellationToken));

            // One background at a time, each in its own guard: a file that cannot be rebuilt
            // (locked, unreadable) must not keep the new logo from every background after it.
            var built = 0;
            foreach (var background in backgrounds)
            {
                try
                {
                    var rendition = await _imageStorage.EnsureEmailLogoRenditionAsync(
                        SourceKey(settings, background.Variant), background.Variant, background.Colour, cancellationToken);
                    if (rendition is not null)
                    {
                        built++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "The {Variant} email logo background for card colour {Colour} could not be " +
                        "rebuilt; mail on that card keeps the logo it had.",
                        background.Variant, background.Colour);
                }
            }

            return built;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Email logo backgrounds could not be rebuilt. Emails keep the logo they had, or " +
                "show the platform name as text where none was built.");
            return null;
        }
    }

    private static EmailLogoBackground[] BackgroundsOf(CardColors colours) =>
    [
        new(EmailLogoVariant.Light, colours.Light),
        new(EmailLogoVariant.Dark, colours.Dark)
    ];

    /// <summary>The light logo is built from the platform logo, the dark one from the dark logo.</summary>
    private static string? SourceKey(PlatformSettings settings, EmailLogoVariant variant) =>
        variant == EmailLogoVariant.Dark ? settings.LogoUrlDark : settings.LogoUrl;
}
