using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;

/// <summary>
/// Command to save draft edits: upsert/remove translations and set the change
/// note. Creates the draft version lazily (clone of the published version) when
/// none is pending. Optimistic concurrency via ExpectedModifiedAt.
/// </summary>
public record UpdateNotificationTemplateDraftCommand(
    Guid TemplateId,
    List<DraftTranslationInput> Translations,
    List<string>? RemoveLanguages = null,
    string? ChangeNote = null,
    DateTime? ExpectedModifiedAt = null) : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid ModifiedBy { get; init; }
}

/// <summary>
/// One translation upsert within a draft save.
/// </summary>
public record DraftTranslationInput(
    string LanguageCode,
    string Subject,
    string BodyHtml,
    string? BodyText = null);
