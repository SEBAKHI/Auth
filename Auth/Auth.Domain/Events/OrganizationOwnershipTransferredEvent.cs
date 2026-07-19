using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when organization ownership has been transferred to a new owner.
/// </summary>
public record OrganizationOwnershipTransferredEvent(
    Guid OrganizationId,
    string OrganizationName,
    Guid PreviousOwnerId,
    Guid NewOwnerId,
    Guid TransferredBy,
    bool ViaPlatformScope) : IDomainEvent;
