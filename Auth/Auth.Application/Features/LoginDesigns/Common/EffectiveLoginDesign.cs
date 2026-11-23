using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>
/// The one place that decides which design a sign-in page shows: the application's active
/// design, else the platform's active design while the platform shows the panel
/// (<see cref="PlatformSettings.LoginPanelShown"/>), else none. The branding payloads and the
/// public render both ask it, so they can never disagree.
/// </summary>
public sealed class EffectiveLoginDesign
{
    private readonly ILoginDesignRepository _designs;
    private readonly IPlatformSettingsRepository _platformSettings;

    public EffectiveLoginDesign(ILoginDesignRepository designs, IPlatformSettingsRepository platformSettings)
    {
        _designs = designs;
        _platformSettings = platformSettings;
    }

    /// <summary>The design shown on a page of <paramref name="applicationId"/> (null: the platform's own pages).</summary>
    public async Task<LoginDesign?> ForAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        if (applicationId is { } id && await _designs.GetActiveAsync(id, cancellationToken) is { } own)
        {
            return own;
        }

        // A hidden panel offers nothing in the platform's name: no page has a panel to fill.
        var settings = await _platformSettings.GetAsync(cancellationToken);
        if (settings is { LoginPanelShown: false })
        {
            return null;
        }

        return await _designs.GetActiveAsync(null, cancellationToken);
    }
}
