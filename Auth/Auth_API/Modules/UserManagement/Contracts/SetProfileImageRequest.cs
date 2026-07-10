namespace Auth_API.Modules.UserManagement.Contracts;

/// <summary>Persists an already-uploaded image (its storage key) as a user's profile image.</summary>
public record SetProfileImageRequest(string ImageKey);
