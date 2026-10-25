using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.RemoveMember;

/// <summary>
/// Handler for removing a member from an organization.
/// </summary>
public class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand, ErrorOr<Deleted>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RemoveMemberCommandHandler> _logger;

    public RemoveMemberCommandHandler(
        IOrganizationRepository organizationRepository,
        IPublisher publisher,
        ILogger<RemoveMemberCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _publisher = publisher;
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

        // Removal takes what the membership held: the person's application roles and
        // permissions in this organization are deleted in the same transaction as the
        // membership, so a removed member keeps nothing in the organization's applications
        // and a later rejoin starts from nothing. The grant tables have no foreign key to
        // OrganizationUsers; nothing cascades on its own.
        var removal = await _organizationRepository.RemoveMemberAsync(
            request.OrganizationId, request.UserId, cancellationToken);

        if (!removal.MembershipRemoved)
        {
            // Lost to a concurrent change after the checks above: another removal, or an
            // ownership transfer to this person. The statement deleted nothing.
            var current = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
            return current?.OwnerId == request.UserId
                ? OrganizationErrors.CannotRemoveOwner
                : OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        _logger.LogInformation(
            "Member {UserId} removed from organization {OrganizationId} by {RemovedBy}, with {RoleCount} application roles and {PermissionCount} permissions",
            request.UserId, request.OrganizationId, request.RemovedBy,
            removal.ApplicationRoles.Count, removal.Permissions.Count);

        await _publisher.Publish(
            new OrganizationMemberRemovedEvent(
                request.OrganizationId,
                request.UserId,
                request.RemovedBy,
                removal.ApplicationRoles,
                removal.Permissions),
            cancellationToken);

        return Result.Deleted;
    }
}
