namespace Auth.Application.DTOs;

/// <summary>
/// Provider-agnostic user information extracted from an external auth token.
/// </summary>
/// <param name="ProviderUserId">The user's unique ID from the provider (e.g., Google 'sub' claim).</param>
/// <param name="Email">The user's email address from the provider.</param>
/// <param name="FirstName">The user's first/given name.</param>
/// <param name="LastName">The user's last/family name.</param>
/// <param name="DisplayName">The user's display name (may be null).</param>
/// <param name="PictureUrl">The user's profile picture URL (may be null).</param>
/// <param name="EmailVerified">Whether the provider has verified the email address.</param>
public record ExternalUserInfo(
    string ProviderUserId,
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? PictureUrl,
    bool EmailVerified);
