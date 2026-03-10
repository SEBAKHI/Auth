using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogById;

/// <summary>
/// Handler for getting an audit log entry by ID.
/// </summary>
public class GetAuditLogByIdQueryHandler : IRequestHandler<GetAuditLogByIdQuery, ErrorOr<AuditLogDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetAuditLogByIdQueryHandler> _logger;

    public GetAuditLogByIdQueryHandler(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetAuditLogByIdQueryHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<AuditLogDto>> Handle(GetAuditLogByIdQuery request, CancellationToken cancellationToken)
    {
        var log = await _auditLogRepository.GetByIdAsync(request.Id, cancellationToken);

        if (log == null)
        {
            return AuditLogErrors.NotFound(request.Id);
        }

        var dto = new AuditLogDto
        {
            Id = log.Id,
            UserId = log.UserId,
            ApplicationId = log.ApplicationId,
            ActionType = log.ActionType,
            Action = log.Action,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            OldValues = log.OldValues,
            NewValues = log.NewValues,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            AdditionalData = log.AdditionalData,
            IsSuccess = log.IsSuccess,
            ErrorMessage = log.ErrorMessage,
            Timestamp = log.Timestamp,
            CorrelationId = log.CorrelationId
        };

        // Get user name if available
        if (log.UserId.HasValue)
        {
            var user = await _userRepository.GetByIdAsync(log.UserId.Value, cancellationToken);
            if (user != null)
            {
                dto.UserName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
                dto.UserEmail = user.Email;
            }
        }

        // Get application name if available
        if (log.ApplicationId.HasValue)
        {
            var app = await _applicationRepository.GetByIdAsync(log.ApplicationId.Value, cancellationToken);
            if (app != null)
            {
                dto.ApplicationName = app.Name;
            }
        }

        _logger.LogDebug("Retrieved audit log {AuditLogId}", log.Id);

        return dto;
    }
}
