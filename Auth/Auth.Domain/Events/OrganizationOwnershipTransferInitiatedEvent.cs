using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an organization owner initiates an ownership transfer and a
/// confirmation code is emailed to the prospective new owner.
/// </summary>
public record OrganizationOwnershipTransferInitiatedEvent(
    Guid OrganizationId,
    string OrganizationName,
    Guid TargetUserId,
    Guid InitiatedBy) : IDomainEvent;
