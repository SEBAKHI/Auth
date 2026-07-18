using Auth.Domain.Enums;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a notification template is unpublished (no version is live anymore).
/// </summary>
public record NotificationTemplateUnpublishedEvent(
    Guid TemplateId,
    Guid NotificationTypeId,
    Guid? ApplicationId,
    NotificationChannelType Channel,
    Guid UnpublishedVersionId,
    Guid UnpublishedBy) : IDomainEvent;
