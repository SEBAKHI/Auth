using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.DeleteOrganization;

/// <summary>
/// Handler for deleting an organization.
/// Only the organization owner can delete it.
/// </summary>
public class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand, ErrorOr<Deleted>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<DeleteOrganizationCommandHandler> _logger;

    public DeleteOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        ILogger<DeleteOrganizationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        DeleteOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Only owner can delete
        if (organization.OwnerId != request.RequestedBy)
        {
            return OrganizationErrors.NotOwner;
        }

        // Delete organization (cascades to members, apps, etc. via FK constraints)
        await _organizationRepository.DeleteAsync(request.OrganizationId, cancellationToken);

        _logger.LogInformation(
            "Organization deleted: {OrganizationId} ({OrganizationCode}) by {RequestedBy}",
            organization.Id, organization.Code, request.RequestedBy);

        return Result.Deleted;
    }
}
