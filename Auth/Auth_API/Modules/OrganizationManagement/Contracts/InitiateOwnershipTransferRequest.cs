namespace Auth_API.Modules.OrganizationManagement.Contracts;

/// <summary>
/// Request to initiate an ownership transfer: emails a confirmation code to
/// the prospective new owner.
/// </summary>
public record InitiateOwnershipTransferRequest(Guid NewOwnerId);
