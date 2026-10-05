using Auth.Application.Configuration;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// Answers what the organization-creation page may offer. Reads only.
/// </summary>
public class GetOrganizationSetupQueryHandler
    : IRequestHandler<GetOrganizationSetupQuery, ErrorOr<OrganizationSetupStateResult>>
{
    private readonly OrganizationSetupSession _setupSession;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly OrganizationSettings _settings;

    public GetOrganizationSetupQueryHandler(
        OrganizationSetupSession setupSession,
        IOrganizationRepository organizationRepository,
        IOptionsSnapshot<OrganizationSettings> settings)
    {
        _setupSession = setupSession;
        _organizationRepository = organizationRepository;
        _settings = settings.Value;
    }

    public async Task<ErrorOr<OrganizationSetupStateResult>> Handle(
        GetOrganizationSetupQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _setupSession.ResolveUserAsync(request.IdpSessionToken, cancellationToken);
        if (user is null)
        {
            return OrganizationSetupStateResult.SignIn;
        }

        var context = await _setupSession.ResolveContextAsync(user, request.ClientId, cancellationToken);
        if (context.IsError)
        {
            return context.Errors;
        }

        var limit = _settings.MaxSelfServiceOrganizationsPerUser;
        var owned = await _organizationRepository.CountSelfServiceOwnedAsync(user.Id, cancellationToken);

        var candidates = await _organizationRepository.GetOrganizationSetupCandidatesAsync(
            user.Id, context.Value.Application.Id, context.Value.CreatorRoleId, cancellationToken);

        return new OrganizationSetupStateResult(
            SignInRequired: false,
            new OrganizationSetupState(
                CanCreate: owned < limit,
                Limit: limit,
                OwnedOrganizations: candidates
                    .Select(candidate => new OrganizationSetupOption(candidate.Id, candidate.Name))
                    .ToList(),
                Email: user.Email.Value));
    }
}
