using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an administrator saves or resets a system-settings section.
/// Payloads are the sparse override JSON before and after the change; secret
/// values can never appear here because writes are whitelisted against the
/// settings registry, so both payloads are safe to audit verbatim.
/// A reset publishes "{}" as the new payload.
/// </summary>
public record SystemSettingsUpdatedEvent(
    string SectionKey,
    string OldOverridesJson,
    string NewOverridesJson,
    Guid UpdatedBy) : IDomainEvent;
