namespace Auth.Application.DTOs;

/// <summary>
/// Section overview for notifications: what exists, what is live, and how
/// delivery is going — in one payload, so the landing page is one request.
/// </summary>
public class NotificationsSummaryDto
{
    public NotificationTemplatesSummaryDto Templates { get; set; } = new();
    public NotificationLayoutsSummaryDto Layouts { get; set; } = new();
    public NotificationOutboxSummaryDto Outbox { get; set; } = new();

    /// <summary>
    /// The most recently changed published templates, with the version that is
    /// actually live. Capped — the full list lives on the templates tab.
    /// </summary>
    public List<PublishedNotificationTemplateDto> PublishedTemplates { get; set; } = [];

    /// <summary>
    /// Every layout with its published state; layouts are unique per
    /// application and channel, so this list stays short by construction.
    /// </summary>
    public List<PublishedNotificationLayoutDto> PublishedLayouts { get; set; } = [];
}

/// <summary>Template counts by publication state and channel.</summary>
public class NotificationTemplatesSummaryDto
{
    public int Total { get; set; }
    public int Published { get; set; }

    /// <summary>Templates carrying an unpublished draft version.</summary>
    public int Drafts { get; set; }

    /// <summary>Count per channel name, e.g. <c>Email</c>.</summary>
    public Dictionary<string, int> ByChannel { get; set; } = [];
}

/// <summary>Layout counts by publication state.</summary>
public class NotificationLayoutsSummaryDto
{
    public int Total { get; set; }
    public int Published { get; set; }
}

/// <summary>Delivery-log counts by status.</summary>
public class NotificationOutboxSummaryDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Last24Hours { get; set; }
}

/// <summary>One live template and the version that is serving.</summary>
public class PublishedNotificationTemplateDto
{
    public Guid Id { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? ApplicationName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public int? PublishedVersionNumber { get; set; }
    public bool HasUnpublishedDraft { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>One layout and whether its draft has been published.</summary>
public class PublishedNotificationLayoutDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ApplicationName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool HasUnpublishedChanges { get; set; }
    public DateTime? PublishedAt { get; set; }
}
