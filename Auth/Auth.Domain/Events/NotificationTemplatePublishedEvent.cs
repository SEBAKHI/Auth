using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a notification template version is published (all translations of
/// that version go live atomically).
/// </summary>
public record NotificationTemplatePublishedEvent(
    Guid TemplateId,
    Guid NotificationTypeId,
    Guid? ApplicationId,
    NotificationChannelType Channel,
    Guid VersionId,
    int VersionNumber,
    Guid PublishedBy) : IDomainEvent;
