using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationType;

/// <summary>
/// Command to update a notification type's admin-editable metadata (display
/// fields and preview sample data; code and system flag are immutable).
/// </summary>
public record UpdateNotificationTypeCommand(
    Guid TypeId,
    string Name,
    string? Description,
    string VariablesJson,
    string SampleDataJson) : IRequest<ErrorOr<NotificationTypeDto>>
{
    public Guid ModifiedBy { get; init; }
}
