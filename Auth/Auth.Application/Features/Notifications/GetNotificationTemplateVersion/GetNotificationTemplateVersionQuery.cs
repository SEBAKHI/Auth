using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplateVersion;

/// <summary>
/// Query for one historical version's full translations (history preview/restore).
/// </summary>
public record GetNotificationTemplateVersionQuery(Guid TemplateId, Guid VersionId)
    : IRequest<ErrorOr<NotificationTemplateVersionDto>>;
