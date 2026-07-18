using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTypes;

/// <summary>
/// Handler returning all notification types.
/// </summary>
public class GetNotificationTypesQueryHandler
    : IRequestHandler<GetNotificationTypesQuery, ErrorOr<List<NotificationTypeDto>>>
{
    private readonly INotificationTypeRepository _typeRepository;

    public GetNotificationTypesQueryHandler(INotificationTypeRepository typeRepository)
    {
        _typeRepository = typeRepository;
    }

    public async Task<ErrorOr<List<NotificationTypeDto>>> Handle(
        GetNotificationTypesQuery request,
        CancellationToken cancellationToken)
    {
        var types = await _typeRepository.GetAllAsync(cancellationToken);
        return types.Select(NotificationMapping.ToTypeDto).ToList();
    }
}
