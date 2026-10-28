namespace Auth.Application.DTOs;

/// <summary>
/// DTO for a notification layout (shared visual identity per application/channel).
/// </summary>
public class NotificationLayoutDto
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DraftContent { get; set; } = string.Empty;
    public string DraftStringsJson { get; set; } = "{}";

    /// <summary>The draft card colour for light mode (#RRGGBB), or null for the default.</summary>
    public string? DraftCardColorLight { get; set; }

    /// <summary>The draft card colour for dark mode (#RRGGBB), or null for the default.</summary>
    public string? DraftCardColorDark { get; set; }
    public bool IsPublished { get; set; }
    public bool HasUnpublishedChanges { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
