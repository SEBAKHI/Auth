using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Users.GetMyApplications;

/// <summary>
/// Handler for <see cref="GetMyApplicationsQuery"/>.
/// </summary>
/// <remarks>
/// Two existing rules answer the two questions, and this handler decides neither: who may sign
/// in is the sign-in gate's predicate (<see cref="IApplicationAccessRepository.GetApplicationsForUserAsync"/>),
/// and where a tile may send the browser is <see cref="ApplicationLaunchTarget.ForHomeTile"/>. An entitled
/// application with no way in (a machine client, no safe address) is left out rather than shown
/// as a tile that cannot open (owner decision D9).
/// </remarks>
public class GetMyApplicationsQueryHandler
    : IRequestHandler<GetMyApplicationsQuery, ErrorOr<IReadOnlyList<MyApplicationDto>>>
{
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly ApplicationLaunchTarget _launchTarget;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IdentityProviderSettings _identityProviderSettings;

    public GetMyApplicationsQueryHandler(
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

    public async Task<ErrorOr<IReadOnlyList<MyApplicationDto>>> Handle(
        GetMyApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var accesses = await _applicationAccessRepository.GetApplicationsForUserAsync(
            request.UserId, cancellationToken);

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
