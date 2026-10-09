using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when the platform appearance (colours, radius, menu accent) is replaced.
/// </summary>
public record PlatformThemeUpdatedEvent(
    Guid SettingsId,
    PlatformTheme OldTheme,
    PlatformTheme NewTheme,
    Guid UpdatedBy) : IDomainEvent;
