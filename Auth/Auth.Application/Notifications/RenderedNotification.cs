using Auth.Domain.Enums;

namespace Auth.Application.Notifications;

/// <summary>
/// A fully rendered notification ready for channel delivery: final subject and
/// bodies with the layout applied and all variables resolved.
/// </summary>
public record RenderedNotification
{
    public required NotificationChannelType Channel { get; init; }
    public required string RecipientAddress { get; init; }
    public string? RecipientName { get; init; }

    /// <summary>
    /// Gets the notification type the content was rendered for (<c>NotificationTypeCodes</c>),
    /// so a channel can treat a type that must really be delivered differently from
    /// the rest (<c>NotificationTypeCodes.DeliveryRequiredCodes</c>). Null for a
    /// preview, which belongs to no send.
    /// </summary>
    public string? NotificationTypeCode { get; init; }

    /// <summary>
    /// Gets the language the notification was rendered in (after the fallback chain).
    /// </summary>
    public required string LanguageCode { get; init; }

    public required string Subject { get; init; }
    public required string BodyHtml { get; init; }
    public required string BodyText { get; init; }

    /// <summary>
    /// Gets the template version that produced this content (diagnostics/outbox audit).
    /// </summary>
    public Guid? TemplateId { get; init; }
    public Guid? TemplateVersionId { get; init; }

    /// <summary>
    /// Gets the human-readable version number that produced this content.
    /// </summary>
    public int? TemplateVersionNumber { get; init; }
}
