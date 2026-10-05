using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised after an application's organization-creation step committed: the
/// user's organization has the application enabled and the user holds the
/// application's creator role in it.
/// </summary>
/// <param name="OrganizationCreated">
/// True when the organization was created by this step; false when the user
/// chose an organization they already owned.
/// </param>
/// <param name="OccurredAtUtc">When the transaction committed.</param>
public record OrganizationProvisionedForApplicationEvent(
    Guid OrganizationId,
    Guid UserId,
    Guid ApplicationId,
    Guid CreatorRoleId,
    bool OrganizationCreated,
    DateTime OccurredAtUtc) : IDomainEvent;
