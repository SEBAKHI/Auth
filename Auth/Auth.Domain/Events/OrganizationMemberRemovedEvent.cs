using Auth.Domain.Primitives;
using Auth.Domain.ReadModels.Organizations;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a member is removed from an organization: raised only when the membership
/// row was removed. Carries the application roles and permissions the removal deleted
/// with it, so one removal is one audit row that lists what went with it.
/// </summary>
public record OrganizationMemberRemovedEvent(
    Guid OrganizationId,
    Guid UserId,
    Guid RemovedBy,
    IReadOnlyList<RemovedOrganizationApplicationRole> RemovedApplicationRoles,
    IReadOnlyList<RemovedOrganizationPermission> RemovedPermissions) : IDomainEvent;
