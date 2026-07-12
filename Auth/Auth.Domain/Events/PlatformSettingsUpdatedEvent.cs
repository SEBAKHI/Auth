using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the platform branding settings are updated.
/// </summary>
public record PlatformSettingsUpdatedEvent(
    Guid SettingsId,
    string OldPlatformName,
    string NewPlatformName,
    string? OldLogoUrl,
    string? NewLogoUrl,
    string? OldLogoUrlDark,
    string? NewLogoUrlDark,
    string? OldFaviconUrl,
    string? NewFaviconUrl,
    Guid UpdatedBy) : IDomainEvent;
