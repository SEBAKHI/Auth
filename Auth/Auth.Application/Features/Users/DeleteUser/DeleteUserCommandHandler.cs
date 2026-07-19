using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.DeleteUser;

/// <summary>
/// Handler for deleting a user.
/// </summary>
public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        IPublisher publisher,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        // Cannot delete system users
        if (user.IsSystemUser)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        // Deleting an owner while other members still depend on the
        // organization would orphan it — no one could transfer or manage it
        // again. So block only when an owned organization still has OTHER active
        // members: a real one must be transferred first, an (untransferable)
        // personal one must have its members removed. Organizations the user
        // solely owns carry no one else, so they are deleted together with the
        // account (the caller is warned of this in the UI) rather than orphaned.
        var ownedOrganizations = await _organizationRepository.GetByOwnerAsync(request.Id, cancellationToken);
        if (ownedOrganizations.Count > 0)
        {
            var ownedOrgIds = ownedOrganizations.Select(o => o.Id).ToList();
            var memberCounts = await _organizationRepository.GetMemberCountsAsync(ownedOrgIds, cancellationToken);

            // A count > 1 means members beyond the owner's own membership.
            var blockingOrg = ownedOrganizations
                .FirstOrDefault(o => memberCounts.GetValueOrDefault(o.Id) > 1);
            if (blockingOrg is not null)
            {
                return blockingOrg.IsAutoCreated
                    ? UserErrors.CannotDeletePersonalOrganizationWithMembers
                    : UserErrors.CannotDeleteOrganizationOwner;
            }

            // All owned organizations are sole-member: remove them with the
            // account (hard delete cascades memberships, subscriptions, codes).
            foreach (var org in ownedOrganizations)
            {
                await _organizationRepository.DeleteAsync(org.Id, cancellationToken);
                _logger.LogInformation(
                    "Organization {OrganizationId} deleted as part of owner account deletion: {UserId}",
                    org.Id, request.Id);
            }
        }

        await _userRepository.DeleteAsync(request.Id, cancellationToken);

        _logger.LogInformation(
            "User deleted: {UserId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        await _publisher.Publish(
            new UserDeletedEvent(request.Id, user.Email, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
