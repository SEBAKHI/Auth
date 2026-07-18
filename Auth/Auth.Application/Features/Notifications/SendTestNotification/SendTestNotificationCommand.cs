using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.SendTestNotification;

/// <summary>
/// Command to send a real test message rendered from a template version with the
/// type's sample data. VersionId null = the pending draft when present, else the
/// published version. Honors Email.Enabled (logs instead of sending in dev).
/// </summary>
public record SendTestNotificationCommand(
    Guid TemplateId,
    string LanguageCode,
    string RecipientEmail,
    Guid? VersionId = null) : IRequest<ErrorOr<Success>>
{
    public Guid RequestedBy { get; init; }
}
