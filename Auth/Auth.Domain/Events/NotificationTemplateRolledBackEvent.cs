using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a notification template's published pointer is rolled back to a
/// previous version (all translations of that version return atomically).
/// </summary>
public record NotificationTemplateRolledBackEvent(
    Guid TemplateId,
    Guid NotificationTypeId,
    Guid? ApplicationId,
    NotificationChannelType Channel,
    Guid? FromVersionId,
    Guid ToVersionId,
    int ToVersionNumber,
    Guid RolledBackBy) : IDomainEvent;
