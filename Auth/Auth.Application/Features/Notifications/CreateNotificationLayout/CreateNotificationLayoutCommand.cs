using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.CreateNotificationLayout;

/// <summary>
/// Command to create an application-specific layout (unpublished until its first
/// publish; sends fall back to the global layout meanwhile).
/// </summary>
public record CreateNotificationLayoutCommand(
    Guid? ApplicationId,
    NotificationChannelType Channel,
    string Name,
    string DraftContent,
    string DraftStringsJson) : IRequest<ErrorOr<NotificationLayoutDto>>
{
    public Guid CreatedBy { get; init; }
}
