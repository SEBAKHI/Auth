using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Domain.Entities;

namespace Auth.Application.Features.Notifications.Common;

/// <summary>
/// Shared aggregate-to-DTO mapping and sample-data parsing for the notification
/// feature handlers (single implementation — several commands return the detail DTO).
/// </summary>
public static class NotificationMapping
{
    public static NotificationTemplateDetailDto ToDetailDto(
        NotificationTemplate template,
        NotificationType type,
        string? applicationName)
    {
        return new NotificationTemplateDetailDto
        {
            Id = template.Id,
            NotificationTypeId = template.NotificationTypeId,
            TypeCode = type.Code,
            TypeName = type.Name,
            TypeIsSystem = type.IsSystem,
            TypeVariablesJson = type.VariablesJson,
            TypeSampleDataJson = type.SampleDataJson,
            ApplicationId = template.ApplicationId,
            ApplicationName = applicationName,
            Channel = template.Channel.ToString(),
            DefaultLanguage = template.DefaultLanguage,
            PublishedVersionId = template.PublishedVersionId,
            DraftVersionId = template.DraftVersionId,
            PublishedVersion = template.PublishedVersion is { } published ? ToVersionDto(published) : null,
            DraftVersion = template.DraftVersion is { } draft ? ToVersionDto(draft) : null,
            Versions = template.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new NotificationTemplateVersionSummaryDto
                {
                    Id = v.Id,
                    VersionNumber = v.VersionNumber,
                    ChangeNote = v.ChangeNote,
                    CreatedAt = v.CreatedAt,
                    CreatedBy = v.CreatedBy,
                    IsPublished = v.Id == template.PublishedVersionId,
                    IsDraft = v.Id == template.DraftVersionId,
                    TranslationCount = v.Translations.Count
                })
                .ToList(),
            CreatedAt = template.CreatedAt,
            ModifiedAt = template.ModifiedAt
        };
    }

    public static NotificationTemplateVersionDto ToVersionDto(NotificationTemplateVersion version)
    {
        return new NotificationTemplateVersionDto
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            ChangeNote = version.ChangeNote,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            Translations = version.Translations
                .OrderBy(t => t.LanguageCode, StringComparer.Ordinal)
                .Select(t => new NotificationTranslationDto
                {
                    LanguageCode = t.LanguageCode,
                    Subject = t.Subject,
                    BodyHtml = t.BodyHtml,
                    BodyText = t.BodyText,
                    ModifiedAt = t.ModifiedAt
                })
                .ToList()
        };
    }

    public static NotificationLayoutDto ToLayoutDto(NotificationLayout layout, string? applicationName)
    {
        return new NotificationLayoutDto
        {
            Id = layout.Id,
            ApplicationId = layout.ApplicationId,
            ApplicationName = applicationName,
            Channel = layout.Channel.ToString(),
            Name = layout.Name,
            DraftContent = layout.DraftContent,
            DraftStringsJson = layout.DraftStringsJson,
            IsPublished = layout.IsPublished,
            HasUnpublishedChanges = layout.HasUnpublishedChanges,
            PublishedAt = layout.PublishedAt,
            CreatedAt = layout.CreatedAt,
            ModifiedAt = layout.ModifiedAt
        };
    }

    public static NotificationTypeDto ToTypeDto(NotificationType type)
    {
        return new NotificationTypeDto
        {
            Id = type.Id,
            Code = type.Code,
            Name = type.Name,
            Description = type.Description,
            IsSystem = type.IsSystem,
            VariablesJson = type.VariablesJson,
            SampleDataJson = type.SampleDataJson,
            IsActive = type.IsActive
        };
    }

    /// <summary>
    /// Parses a sample-data JSON object into the renderer's variable dictionary,
    /// converting JSON values to CLR types Fluid renders naturally. Optional
    /// per-preview overrides are merged on top.
    /// </summary>
    public static Dictionary<string, object?> ParseSampleData(string sampleDataJson, string? overridesJson = null)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal);
        Merge(model, sampleDataJson);
        if (!string.IsNullOrWhiteSpace(overridesJson))
        {
            Merge(model, overridesJson);
        }

        return model;
    }

    private static void Merge(Dictionary<string, object?> model, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            model[property.Name] = ToClrValue(property.Value);
        }
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String when element.TryGetDateTime(out var dateTime) => dateTime,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}
