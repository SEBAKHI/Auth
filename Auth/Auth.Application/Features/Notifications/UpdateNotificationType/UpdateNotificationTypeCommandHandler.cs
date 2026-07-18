using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationType;

/// <summary>
/// Handler for updating notification type metadata.
/// </summary>
public class UpdateNotificationTypeCommandHandler
    : IRequestHandler<UpdateNotificationTypeCommand, ErrorOr<NotificationTypeDto>>
{
    private readonly INotificationTypeRepository _typeRepository;
    private readonly ILogger<UpdateNotificationTypeCommandHandler> _logger;

    public UpdateNotificationTypeCommandHandler(
        INotificationTypeRepository typeRepository,
        ILogger<UpdateNotificationTypeCommandHandler> logger)
    {
        _typeRepository = typeRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTypeDto>> Handle(
        UpdateNotificationTypeCommand request,
        CancellationToken cancellationToken)
    {
        var type = await _typeRepository.GetByIdAsync(request.TypeId, cancellationToken);
        if (type is null)
        {
            return NotificationErrors.TypeNotFound(request.TypeId);
        }

        type.Update(request.Name, request.Description, request.VariablesJson, request.SampleDataJson, request.ModifiedBy);
        await _typeRepository.UpdateAsync(type, cancellationToken);

        _logger.LogInformation("Notification type updated: {TypeId} ({TypeCode})", type.Id, type.Code);

        return NotificationMapping.ToTypeDto(type);
    }
}
