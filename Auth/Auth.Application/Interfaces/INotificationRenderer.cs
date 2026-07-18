using Auth.Application.Notifications;
using Auth.Domain.Enums;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Renders notifications from database-managed content: resolves published
/// templates for the send path, and composes arbitrary (draft) content for
/// admin previews — both through the same Liquid pipeline and layout, so a
/// preview is pixel-identical to a real send.
/// </summary>
public interface INotificationRenderer
{
    /// <summary>
    /// Renders a notification from the published template resolved for the
    /// request's (application, type, channel) scope and language chain.
    /// </summary>
    Task<ErrorOr<RenderedNotification>> RenderAsync(NotificationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Renders supplied content (e.g. an unsaved draft translation) with the
    /// published layout of the given scope and the supplied variables.
    /// </summary>
    Task<ErrorOr<RenderedNotification>> RenderContentAsync(
        NotificationContentRenderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// A request to render explicit template content (admin preview / publish validation).
/// </summary>
public record NotificationContentRenderRequest
{
    public NotificationChannelType Channel { get; init; } = NotificationChannelType.Email;

    /// <summary>
    /// Gets the layout scope: app-specific layout preferred, global otherwise.
    /// </summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>
    /// Gets the language to render in (drives direction and culture-aware filters).
    /// </summary>
    public required string LanguageCode { get; init; }

    public required string Subject { get; init; }
    public required string BodyHtml { get; init; }
    public string? BodyText { get; init; }

    /// <summary>
    /// Gets the variable values (typically the type's sample data, optionally
    /// overridden per preview).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// When set, renders inside this explicit layout content instead of the
    /// published layout (used by layout draft previews). StringsJson format
    /// matches NotificationLayout.DraftStringsJson.
    /// </summary>
    public string? LayoutContentOverride { get; init; }
    public string? LayoutStringsJsonOverride { get; init; }

    /// <summary>
    /// When true, every variable the templates reference but the model does not
    /// supply produces an UnknownVariables error (publish-time validation).
    /// </summary>
    public bool FailOnUnknownVariables { get; init; }
}
