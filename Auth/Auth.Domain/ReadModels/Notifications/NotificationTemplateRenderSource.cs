namespace Auth.Domain.ReadModels.Notifications;

/// <summary>
/// Lean read model for the send path: the published version of one template with
/// all its translations, loaded without hydrating the full aggregate. The
/// rendering service picks the translation via the language fallback chain.
/// </summary>
public sealed record NotificationTemplateRenderSource(
    Guid TemplateId,
    Guid PublishedVersionId,
    int PublishedVersionNumber,
    Guid? ApplicationId,
    string DefaultLanguage,
    IReadOnlyList<NotificationTranslationRenderSource> Translations);

/// <summary>
/// A single-language rendering source belonging to a published template version.
/// </summary>
public sealed record NotificationTranslationRenderSource(
    string LanguageCode,
    string Subject,
    string BodyHtml,
    string? BodyText);
