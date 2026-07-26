using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.HardDeleteUser;

/// <summary>
/// Handler for permanently deleting a soft-deleted user.
/// </summary>
public class HardDeleteUserCommandHandler : IRequestHandler<HardDeleteUserCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<HardDeleteUserCommandHandler> _logger;

    public HardDeleteUserCommandHandler(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        IPublisher publisher,
        ILogger<HardDeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(HardDeleteUserCommand request, CancellationToken cancellationToken)
    {
        // The system account is the reattribution target for purged actor
        // references and the author of the seed data — it must never be
        // removable, whatever its flags say.
        if (request.Id == WellKnownUserIds.System)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        var user = await _userRepository.GetByIdIncludeDeletedAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        if (user.IsSystemUser)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        // Hard deletion is a second, separate step: only accounts that already
        // went through the soft delete (hidden, blocked from login) qualify.
        if (!user.IsDeleted)
        {
            return UserErrors.NotSoftDeleted;
        }

        // The soft delete already removed sole-owned organizations, but the
        // ownership FK has no cascade — if an owned organization still exists
        // (legacy data), apply the same rules as the soft delete: block when
        // other members depend on it, remove it when the owner was its only
        // member.
        var ownedOrganizations = await _organizationRepository.GetByOwnerAsync(request.Id, cancellationToken);
        if (ownedOrganizations.Count > 0)
        {
            var ownedOrgIds = ownedOrganizations.Select(o => o.Id).ToList();
            var memberCounts = await _organizationRepository.GetMemberCountsAsync(ownedOrgIds, cancellationToken);

            var blockingOrg = ownedOrganizations
                .FirstOrDefault(o => memberCounts.GetValueOrDefault(o.Id) > 1);
            if (blockingOrg is not null)
            {
                return blockingOrg.IsAutoCreated
                    ? UserErrors.CannotDeletePersonalOrganizationWithMembers
                    : UserErrors.CannotDeleteOrganizationOwner;
            }

            foreach (var org in ownedOrganizations)
            {
                await _organizationRepository.DeleteAsync(org.Id, cancellationToken);
                _logger.LogInformation(
                    "Organization {OrganizationId} deleted as part of permanent account deletion: {UserId}",
                    org.Id, request.Id);
            }
        }

        var purged = await _userRepository.HardDeleteAsync(request.Id, cancellationToken);
        if (!purged)
        {
            // Lost a race with a concurrent write on the account row.
            return UserErrors.NotSoftDeleted;
        }

        _logger.LogInformation(
            "User permanently deleted: {UserId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        await _publisher.Publish(
            new UserHardDeletedEvent(request.Id, user.Email, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
