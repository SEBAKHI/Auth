using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to notification templates, layouts, and rendering.
/// </summary>
public static class NotificationErrors
{
    #region Type Errors

    public static Error TypeNotFound(Guid typeId) => Error.NotFound(
        code: "Notification.TypeNotFound",
        description: $"Notification type with ID '{typeId}' was not found.",
        metadata: new() { ["args"] = new object[] { typeId } });

    public static Error TypeNotFoundByCode(string code) => Error.NotFound(
        code: "Notification.TypeNotFoundByCode",
        description: $"Notification type with code '{code}' was not found.",
        metadata: new() { ["args"] = new object[] { code } });

    #endregion

    #region Template Errors

    public static Error TemplateNotFound(Guid templateId) => Error.NotFound(
        code: "Notification.TemplateNotFound",
        description: $"Notification template with ID '{templateId}' was not found.",
        metadata: new() { ["args"] = new object[] { templateId } });

    public static Error TemplateNotPublished(string typeCode) => Error.NotFound(
        code: "Notification.TemplateNotPublished",
        description: $"No published notification template exists for type '{typeCode}'.",
        metadata: new() { ["args"] = new object[] { typeCode } });

    public static Error DuplicateTemplate => Error.Conflict(
        code: "Notification.DuplicateTemplate",
        description: "A template for this type, application, and channel already exists.");

    public static Error VersionNotFound(Guid versionId) => Error.NotFound(
        code: "Notification.VersionNotFound",
        description: $"Template version with ID '{versionId}' was not found.",
        metadata: new() { ["args"] = new object[] { versionId } });

    public static Error TranslationNotFound(string languageCode) => Error.NotFound(
        code: "Notification.TranslationNotFound",
        description: $"No translation exists for language '{languageCode}'.",
        metadata: new() { ["args"] = new object[] { languageCode } });

    public static Error UnsupportedLanguage(string languageCode) => Error.Validation(
        code: "Notification.UnsupportedLanguage",
        description: $"Language '{languageCode}' is not supported.",
        metadata: new() { ["args"] = new object[] { languageCode } });

    public static Error NoDraftToPublish => Error.Conflict(
        code: "Notification.NoDraftToPublish",
        description: "The template has no pending draft version to publish.");

    public static Error DraftAlreadyExists => Error.Conflict(
        code: "Notification.DraftAlreadyExists",
        description: "The template already has a pending draft. Save or discard it before restoring another version.");

    public static Error NoDraftToDiscard => Error.Conflict(
        code: "Notification.NoDraftToDiscard",
        description: "The template has no pending draft version to discard.");

    public static Error DefaultLanguageTranslationRequired(string languageCode) => Error.Validation(
        code: "Notification.DefaultLanguageTranslationRequired",
        description: $"The draft must contain a translation for the template's default language '{languageCode}'.",
        metadata: new() { ["args"] = new object[] { languageCode } });

    public static Error CannotRemoveDefaultLanguageTranslation(string languageCode) => Error.Validation(
        code: "Notification.CannotRemoveDefaultLanguageTranslation",
        description: $"The default language translation '{languageCode}' cannot be removed.",
        metadata: new() { ["args"] = new object[] { languageCode } });

    public static Error NotPublished => Error.Conflict(
        code: "Notification.NotPublished",
        description: "The template is not currently published.");

    public static Error CannotUnpublishSystemTemplate => Error.Forbidden(
        code: "Notification.CannotUnpublishSystemTemplate",
        description: "The global template of a system notification type cannot be unpublished; critical flows depend on it.");

    public static Error CannotDeleteSystemGlobalTemplate => Error.Forbidden(
        code: "Notification.CannotDeleteSystemGlobalTemplate",
        description: "The global template of a system notification type cannot be deleted; critical flows depend on it.");

    public static Error ConcurrencyConflict => Error.Conflict(
        code: "Notification.ConcurrencyConflict",
        description: "The template was modified by someone else. Reload the latest draft and reapply your changes.");

    public static Error PublishTargetChanged => Error.Conflict(
        code: "Notification.PublishTargetChanged",
        description: "The saved draft selected for publishing has changed. Reload and review the latest draft before publishing.");

    public static Error UnpublishTargetChanged => Error.Conflict(
        code: "Notification.UnpublishTargetChanged",
        description: "The published version selected for unpublishing has changed. Reload and review the current publication before continuing.");

    #endregion

    #region Layout Errors

    public static Error LayoutNotFound(Guid layoutId) => Error.NotFound(
        code: "Notification.LayoutNotFound",
        description: $"Notification layout with ID '{layoutId}' was not found.",
        metadata: new() { ["args"] = new object[] { layoutId } });

    public static Error LayoutNotPublished => Error.NotFound(
        code: "Notification.LayoutNotPublished",
        description: "No published notification layout exists for this scope.");

    public static Error DuplicateLayout => Error.Conflict(
        code: "Notification.DuplicateLayout",
        description: "A layout for this application and channel already exists.");

    public static Error LayoutContentRequired => Error.Validation(
        code: "Notification.LayoutContentRequired",
        description: "The layout content cannot be empty.");

    public static Error LayoutContentSlotMissing => Error.Validation(
        code: "Notification.LayoutContentSlotMissing",
        description: "The layout does not render the message body: it must include the {{ content | raw }} slot, otherwise every message would arrive empty.");

    public static Error LayoutPublishTargetChanged => Error.Conflict(
        code: "Notification.LayoutPublishTargetChanged",
        description: "The saved layout draft selected for publishing has changed. Reload and review the latest draft before publishing.");

    #endregion

    #region Rendering Errors

    public static Error InvalidTemplateSyntax(string details) => Error.Validation(
        code: "Notification.InvalidTemplateSyntax",
        description: $"The template contains invalid syntax: {details}",
        metadata: new() { ["args"] = new object[] { details } });

    public static Error UnknownVariables(string variableNames) => Error.Validation(
        code: "Notification.UnknownVariables",
        description: $"The template references variables that are not in the type's catalog: {variableNames}",
        metadata: new() { ["args"] = new object[] { variableNames } });

    public static Error RenderFailed(string details) => Error.Failure(
        code: "Notification.RenderFailed",
        description: $"Rendering the notification failed: {details}",
        metadata: new() { ["args"] = new object[] { details } });

    public static Error ChannelNotSupported(string channel) => Error.Failure(
        code: "Notification.ChannelNotSupported",
        description: $"No delivery channel implementation is registered for '{channel}'.",
        metadata: new() { ["args"] = new object[] { channel } });

    public static Error SendFailed => Error.Failure(
        code: "Notification.SendFailed",
        description: "The notification could not be delivered.");

    #endregion

    #region Outbox Errors

    public static Error OutboxMessageNotFound(Guid messageId) => Error.NotFound(
        code: "Notification.OutboxMessageNotFound",
        description: $"Outbox message with ID '{messageId}' was not found.",
        metadata: new() { ["args"] = new object[] { messageId } });

    public static Error OutboxMessageNotRetryable => Error.Conflict(
        code: "Notification.OutboxMessageNotRetryable",
        description: "Only failed (Retry or Dead) messages can be requeued.");

    #endregion
}
