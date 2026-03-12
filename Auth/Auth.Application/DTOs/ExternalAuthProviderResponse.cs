namespace Auth.Application.DTOs;

/// <summary>
/// Response DTO for listing available external authentication providers.
/// Used by the frontend to render "Continue with {Name}" buttons.
/// </summary>
/// <param name="Code">The provider code (e.g., "google").</param>
/// <param name="Name">The display name (e.g., "Google").</param>
/// <param name="IconUrl">URL to the provider's icon/logo.</param>
public record ExternalAuthProviderResponse(
    string Code,
    string Name,
    string? IconUrl);
