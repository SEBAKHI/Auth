using Auth.Domain.Enums;

namespace Auth.Application.Notifications;

/// <summary>
/// A request to send one notification. Callers reference the notification type
/// by code and supply the variable values; template resolution, language
/// selection, rendering, and delivery are the notification service's concern.
/// </summary>
public record NotificationRequest
{
    /// <summary>
    /// Gets the notification type code (see Auth.Domain.Constants.NotificationTypeCodes).
    /// </summary>
    public required string TypeCode { get; init; }

    /// <summary>
    /// Gets the delivery channel. Defaults to Email.
    /// </summary>
    public NotificationChannelType Channel { get; init; } = NotificationChannelType.Email;

    /// <summary>
    /// Gets the recipient address for the channel (email address for Email).
    /// </summary>
    public required string RecipientAddress { get; init; }

    /// <summary>
    /// Gets the recipient display name, when known.
    /// </summary>
    public string? RecipientName { get; init; }

    /// <summary>
    /// Gets the recipient's user ID when the recipient has an account. Used to
    /// resolve the notification language from the user's stored PreferredLanguage.
    /// </summary>
    public Guid? RecipientUserId { get; init; }

    /// <summary>
    /// Gets an explicit language override. Takes precedence over the recipient's
    /// profile language. Used e.g. when an inviter chooses the invitee's language.
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Gets a low-priority language hint (e.g. the current request culture) used
    /// only when neither an explicit language nor a profile language is available.
    /// </summary>
    public string? LanguageHint { get; init; }

    /// <summary>
    /// Gets the sending application scope. App-specific templates and layouts are
    /// preferred over global ones for this application; null uses global directly.
    /// </summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>
    /// Gets the actor who triggered the send (recorded as CreatedBy on the
    /// delivery log). For self-service flows this is the recipient themselves;
    /// for invitations it is the inviter.
    /// </summary>
    public Guid? TriggeredBy { get; init; }

    /// <summary>
    /// Gets the variable values injected into the template. Keys must match the
    /// notification type's variable catalog.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; init; } =
        new Dictionary<string, object?>();
}
