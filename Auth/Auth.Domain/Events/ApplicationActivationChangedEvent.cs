using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application is switched on or off. Deactivation locks every
/// user out at once, so it earns its own audit line rather than appearing as a
/// field diff inside a settings update.
/// </summary>
public record ApplicationActivationChangedEvent(
    Guid ApplicationId,
    string ApplicationCode,
    bool IsActive,
    Guid ChangedBy) : IDomainEvent;
