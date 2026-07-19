namespace Auth_API.Modules.OrganizationManagement.Contracts;

/// <summary>
/// Request to complete an ownership transfer. <paramref name="Code"/> is the
/// confirmation code emailed to the new owner; platform administrators
/// transferring someone else's organization omit it.
/// </summary>
public record TransferOwnershipRequest(Guid NewOwnerId, string? Code);
