using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Users.GetMyApplications;

/// <summary>
/// The applications a user can open from a tile: IAM's Home (UI batch item 12) and the account
/// bar a linked application shows. One rule for both, so a tile can never appear in one and not
/// in the other.
/// </summary>
/// <remarks>
/// Two existing rules answer the two questions, and this class decides neither: who may sign
/// in is the sign-in gate's predicate (<see cref="IApplicationAccessRepository.GetApplicationsForUserAsync"/>),
/// and where a tile may send the browser is <see cref="ApplicationLaunchTarget.ForHomeTile"/>. An entitled
/// application with no way in (a machine client, no safe address) is left out rather than shown
/// as a tile that cannot open (owner decision D9).
/// </remarks>
public class MyApplicationsList
{
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly ApplicationLaunchTarget _launchTarget;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IdentityProviderSettings _identityProviderSettings;

    public MyApplicationsList(
        IApplicationAccessRepository applicationAccessRepository,
        ApplicationLaunchTarget launchTarget,
        IImageUrlComposer imageUrlComposer,
        IOptionsSnapshot<IdentityProviderSettings> identityProviderSettings)
    {
        _applicationAccessRepository = applicationAccessRepository;
        _launchTarget = launchTarget;
        _imageUrlComposer = imageUrlComposer;
        _identityProviderSettings = identityProviderSettings.Value;
    }

    /// <summary>The user's tiles, ordered by name.</summary>
    public async Task<IReadOnlyList<MyApplicationDto>> ForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accesses = await _applicationAccessRepository.GetApplicationsForUserAsync(userId, cancellationToken);

        var firstParty = FirstPartyAddresses.From(_identityProviderSettings);

        var applications = new List<MyApplicationDto>(accesses.Count);
        foreach (var access in accesses)
        {
            var launchUrl = _launchTarget.ForHomeTile(
                access.IsActive, access.InitiateLoginUri, access.BaseUrl, access.HasBrowserSignIn);

            // The platform's own browser apps (the console, the accounts app), should one be
            // registered as an application: Home is one of them already (D9).
            if (launchUrl is null || firstParty.Contains(launchUrl))
            {
                continue;
            }

            applications.Add(new MyApplicationDto
            {
                Id = access.ApplicationId,
                Name = access.Name,
                LogoUrl = _imageUrlComposer.Compose(access.LogoUrl),
                LogoUrlDark = _imageUrlComposer.Compose(access.LogoUrlDark),
                LaunchUrl = launchUrl
            });
        }

        // The repository orders by name, so the grid reads the same on every visit.
        return applications;
    }
}
