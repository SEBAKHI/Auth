using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// Runs the organization-creation step for an application.
/// </summary>
/// <remarks>
/// Checks, in order, each with its own code: a usable sign-in session (401),
/// the application offers the step (<c>Organization.CreationFromApplicationUnavailable</c>),
/// the email is proven (<c>User.EmailNotConfirmed</c>), then the name or the
/// chosen organization, and the per-user limit, which the repository counts
/// under a lock inside the same transaction that writes.
/// </remarks>
public class SetUpOrganizationCommandHandler
    : IRequestHandler<SetUpOrganizationCommand, ErrorOr<SetUpOrganizationResult>>
{
    /// <summary>RFC 4648 base32, lower case: letters and the digits 2 to 7.</summary>
    private const string CodeAlphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private const int CodeRandomLength = 10;

    private const int MaxCodeAttempts = 3;

    private readonly OrganizationSetupSession _setupSession;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPublisher _publisher;
    private readonly OrganizationSettings _settings;
    private readonly ILogger<SetUpOrganizationCommandHandler> _logger;

    public SetUpOrganizationCommandHandler(
        OrganizationSetupSession setupSession,
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IPublisher publisher,
        IOptionsSnapshot<OrganizationSettings> settings,
        ILogger<SetUpOrganizationCommandHandler> logger)
    {
        _setupSession = setupSession;
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<SetUpOrganizationResult>> Handle(
        SetUpOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _setupSession.ResolveUserAsync(request.IdpSessionToken, cancellationToken);
        if (user is null)
        {
            return SetUpOrganizationResult.SignIn;
        }

        var context = await _setupSession.ResolveContextAsync(user, request.ClientId, cancellationToken);
        if (context.IsError)
        {
            return context.Errors;
        }

        var application = context.Value.Application;
        var creatorRoleId = context.Value.CreatorRoleId;

        var outcome = request.OrganizationId is Guid existingId
            ? await _organizationRepository.ProvisionForApplicationAsync(
                OrganizationProvisioningRequest.ForExistingOrganization(
                    existingId, user.Id, application.Id, creatorRoleId),
                cancellationToken)
            : await CreateAsync(user, application.Id, creatorRoleId, request.Name!, cancellationToken);

        if (outcome.IsError)
        {
            return outcome.Errors;
        }

        switch (outcome.Value.Status)
        {
            case OrganizationProvisioningStatus.Provisioned:
                var organizationId = outcome.Value.OrganizationId!.Value;
                _logger.LogInformation(
                    "Organization {OrganizationId} set up for application {ApplicationId} by user {UserId} (created: {Created})",
                    organizationId, application.Id, user.Id, outcome.Value.OrganizationCreated);

                // After the commit, never before: a handler that fails here must
                // not leave an audit row for an organization that does not exist.
                await _publisher.Publish(
                    new OrganizationProvisionedForApplicationEvent(
                        organizationId,
                        user.Id,
                        application.Id,
                        creatorRoleId,
                        outcome.Value.OrganizationCreated,
                        DateTime.UtcNow),
                    cancellationToken);

                return new SetUpOrganizationResult(false, organizationId);

            case OrganizationProvisioningStatus.AlreadySetUp:
                // A second submit, a double click, or a return through authorize:
                // nothing was written, and the answer is the same as the first time.
                return new SetUpOrganizationResult(false, outcome.Value.OrganizationId);

            case OrganizationProvisioningStatus.LimitReached:
                _logger.LogInformation(
                    "Organization creation from application {ApplicationId} refused for user {UserId}: limit of {Limit} reached",
                    application.Id, user.Id, _settings.MaxSelfServiceOrganizationsPerUser);
                return OrganizationErrors.SelfServiceLimitReached;

            default:
                // Not the user's, a personal organization, or one that cannot be set
                // up: the same answer as a nonexistent id, so another user's
                // organization is never confirmed to exist.
                return request.OrganizationId is Guid notFoundId
                    ? OrganizationErrors.NotFound(notFoundId)
                    : OrganizationErrors.CreationFromApplicationUnavailable;
        }
    }

    private async Task<ErrorOr<OrganizationProvisioningOutcome>> CreateAsync(
        User user,
        Guid applicationId,
        Guid creatorRoleId,
        string name,
        CancellationToken cancellationToken)
    {
        // The role CreateOrganizationCommandHandler binds to an owner's membership.
        var ownerRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Owner, cancellationToken);
        if (ownerRole is null)
        {
            _logger.LogError("Organization owner role '{RoleCode}' not found in database", OrganizationRoleCodes.Owner);
            return OrganizationErrors.OwnerRoleNotFound;
        }

        for (var attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            var organization = Organization.Create(
                code: GenerateCode(),
                name: name,
                contactEmail: user.Email.Value,
                ownerId: user.Id);

            var outcome = await _organizationRepository.ProvisionForApplicationAsync(
                OrganizationProvisioningRequest.ForNewOrganization(
                    organization,
                    applicationId,
                    creatorRoleId,
                    ownerRole.Id,
                    _settings.MaxSelfServiceOrganizationsPerUser),
                cancellationToken);

            if (outcome.Status != OrganizationProvisioningStatus.CodeTaken)
            {
                return outcome;
            }

            _logger.LogWarning("Generated organization code collided (attempt {Attempt})", attempt);
        }

        // 32^10 codes: three collisions in a row mean something other than chance.
        throw new InvalidOperationException(
            $"No free organization code after {MaxCodeAttempts} attempts.");
    }

    /// <summary>
    /// <c>org-</c> and ten random base32 characters. ASCII only and never derived
    /// from the name, which may be Arabic: the code is a URL slug and must pass
    /// the console's own code rule (<c>^[a-zA-Z0-9_-]+$</c>, at most 50).
    /// </summary>
    internal static string GenerateCode()
    {
        Span<char> random = stackalloc char[CodeRandomLength];
        for (var i = 0; i < random.Length; i++)
        {
            random[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return "org-" + new string(random);
    }
}
