using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationOutboxMessages;

/// <summary>
/// Query for the paged notification delivery log with status/channel filters
/// and recipient/type/subject search.
/// </summary>
public record GetNotificationOutboxMessagesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    NotificationDeliveryStatus? Status = null,
    NotificationChannelType? Channel = null,
    string? SearchTerm = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Desc) : IRequest<ErrorOr<PagedNotificationOutboxDto>>;
