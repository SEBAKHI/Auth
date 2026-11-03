using Auth.Application.Interfaces;
using Auth.Domain.Entities;

namespace Auth.Application.Common;

/// <summary>
/// Rebuilds the tab icon both applications' HTML names (<see cref="IImageStorageService.EnsureTabIconRenditionAsync"/>)
/// from the platform's current images: when an administrator saves Platform settings, and at
/// startup for an install whose images predate the file.
/// </summary>
/// <remarks>
/// Best-effort, like <see cref="EmailLogoRenditions"/>: a storage fault never fails the admin
/// action that triggered it, and browsers keep the icon the file last held.
/// </remarks>
public class PlatformTabIcon
{
    private readonly IImageStorageService _imageStorage;
    private readonly ILogger<PlatformTabIcon> _logger;

    public PlatformTabIcon(IImageStorageService imageStorage, ILogger<PlatformTabIcon> logger)
    {
        _imageStorage = imageStorage;
        _logger = logger;
    }

    /// <summary>Rebuilds the icon; false when it could not be written.</summary>
    public async Task<bool> RebuildAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return await _imageStorage.EnsureTabIconRenditionAsync(settings.TabIconKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The tab icon could not be rebuilt; browsers keep the icon it had.");
            return false;
        }
    }
}
