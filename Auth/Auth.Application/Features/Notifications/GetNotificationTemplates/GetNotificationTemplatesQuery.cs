using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplates;

/// <summary>
/// Query for the paged admin list of notification templates.
/// </summary>
public record GetNotificationTemplatesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? NotificationTypeId = null,
    Guid? ApplicationId = null,
    NotificationChannelType? Channel = null,
    bool? IsPublished = null,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedNotificationTemplatesDto>>;
