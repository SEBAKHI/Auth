using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Users.Common;

/// <summary>
/// Shared owned-organization rule for every account deletion flow (admin soft
/// delete, permanent purge, self-service deletion): deleting an owner while
/// other members still depend on the organization would orphan it — no one
/// could transfer or manage it again. So deletion is blocked while an owned
/// organization has OTHER active members (a real one must be transferred
/// first, an untransferable personal one must have its members removed), and
/// organizations the user solely owns are deleted together with the account
/// rather than orphaned.
/// </summary>
public class OwnedOrganizationDeletionGuard
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<OwnedOrganizationDeletionGuard> _logger;

    public OwnedOrganizationDeletionGuard(
        IOrganizationRepository organizationRepository,
        ILogger<OwnedOrganizationDeletionGuard> logger)
    {
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    /// <summary>
    /// Blocks when an owned organization still has other members; deletes
    /// sole-member owned organizations (the caller is warned of this in the UI).
    /// </summary>
    /// <param name="userId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<Success>> EnsureDeletableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ownedOrganizations = await _organizationRepository.GetByOwnerAsync(userId, cancellationToken);
        if (ownedOrganizations.Count == 0)
        {
            return Result.Success;
        }

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
                "Organization {OrganizationId} deleted as part of account deletion: {UserId}",
                org.Id, userId);
        }

        return Result.Success;
    }
}
