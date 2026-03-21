namespace Auth_API.Modules.UserManagement.Contracts;

public record LockAccountRequest(
    string Reason,
    int? LockDurationMinutes = null);
