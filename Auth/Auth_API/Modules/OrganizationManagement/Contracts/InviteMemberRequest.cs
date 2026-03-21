namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record InviteMemberRequest(string Email, Guid RoleId);
