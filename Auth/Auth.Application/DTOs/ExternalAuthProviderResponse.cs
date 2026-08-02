namespace Auth.Application.DTOs;

/// <summary>
/// Response DTO for listing available external authentication providers.
/// Used by the frontend to render "Continue with {Name}" buttons.
/// </summary>
/// <param name="Code">The provider code (e.g., "google").</param>
/// <param name="Name">The display name (e.g., "Google").</param>
/// <param name="IconUrl">URL to the provider's icon/logo.</param>
/// <param name="ClientId">
/// The provider's PUBLIC client identifier — Google's OAuth client id, Apple's
/// Services ID. It is not a secret (it ships inside every sign-in page), and it
/// must be the same value the API validates the returned token's audience
/// against. Serving it here is what keeps the two in step: the client that MINTS
/// the token and the server that VERIFIES it now read one source of truth, so a
/// client id changed in the system-settings console applies without rebuilding
/// and redeploying the SPA.
/// </param>
public record ExternalAuthProviderResponse(
    string Code,
    string Name,
    string? IconUrl,
    string ClientId);
