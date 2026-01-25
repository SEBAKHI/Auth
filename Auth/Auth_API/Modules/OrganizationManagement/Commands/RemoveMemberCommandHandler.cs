using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Handler for removing a member from an organization.
/// </summary>
public class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand, ErrorOr<Deleted>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<RemoveMemberCommandHandler> _logger;

    public RemoveMemberCommandHandler(
        IOrganizationRepository organizationRepository,
        ILogger<RemoveMemberCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        RemoveMemberCommand request,
        CancellationToken cancellationToken)
    {
        // Get organization
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Cannot remove the owner
        if (organization.OwnerId == request.UserId)
        {
            return OrganizationErrors.CannotRemoveOwner;
        }

        // Check if user is a member
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (membership == null)
        {
            return OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        // Remove member (cascades to app roles and permissions via FK)
        await _organizationRepository.RemoveMemberAsync(request.OrganizationId, request.UserId, cancellationToken);

        _logger.LogInformation(
            "Member {UserId} removed from organization {OrganizationId} by {RemovedBy}",
            request.UserId, request.OrganizationId, request.RemovedBy);

        return Result.Deleted;
    }
}
