using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;

/// <summary>
/// Handler for getting audit logs for a specific entity.
/// </summary>
public class GetAuditLogsByEntityQueryHandler : IRequestHandler<GetAuditLogsByEntityQuery, ErrorOr<IReadOnlyList<AuditLogDto>>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetAuditLogsByEntityQueryHandler> _logger;

    public GetAuditLogsByEntityQueryHandler(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetAuditLogsByEntityQueryHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<AuditLogDto>>> Handle(GetAuditLogsByEntityQuery request, CancellationToken cancellationToken)
    {
        var logs = await _auditLogRepository.GetByEntityAsync(
            request.EntityType,
            request.EntityId,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var dtos = new List<AuditLogDto>();
        foreach (var log in logs)
        {
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
                var app = await _applicationRepository.GetByIdIncludingDeletedAsync(log.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug(
            "Retrieved {Count} audit logs for entity {EntityType}/{EntityId}",
            dtos.Count, request.EntityType, request.EntityId);

        return dtos;
    }
}
