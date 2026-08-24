using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a role is renamed or its description changes. Carries both sides so the audit row can say what it was before.
/// </summary>
public record RoleUpdatedEvent(
    Guid RoleId,
    string RoleCode,
    string OldName,
    string NewName,
    string? OldDescription,
    string? NewDescription,
    Guid UpdatedBy) : IDomainEvent;
