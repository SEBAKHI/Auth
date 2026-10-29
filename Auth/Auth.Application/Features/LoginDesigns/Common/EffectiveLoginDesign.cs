using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>
/// The one place that decides which design a sign-in page shows: the application's active
/// design, else the platform's active design, else none (the plain panel). The branding
/// payloads and the public render both ask it, so they can never disagree.
/// </summary>
public sealed class EffectiveLoginDesign
{
    private readonly ILoginDesignRepository _designs;

    public EffectiveLoginDesign(ILoginDesignRepository designs)
    {
        _designs = designs;
    }

    /// <summary>The design shown on a page of <paramref name="applicationId"/> (null: the platform's own pages).</summary>
    public async Task<LoginDesign?> ForAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        if (applicationId is { } id && await _designs.GetActiveAsync(id, cancellationToken) is { } own)
        {
            return own;
        }

        return await _designs.GetActiveAsync(null, cancellationToken);
    }
}
