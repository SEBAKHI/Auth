using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.AuditLogs.GetAuditLogsByUser;

/// <summary>
/// Handler for getting audit logs for a specific user.
/// </summary>
public class GetAuditLogsByUserQueryHandler : IRequestHandler<GetAuditLogsByUserQuery, ErrorOr<PagedAuditLogsDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetAuditLogsByUserQueryHandler> _logger;

    public GetAuditLogsByUserQueryHandler(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetAuditLogsByUserQueryHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedAuditLogsDto>> Handle(GetAuditLogsByUserQuery request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var (logs, totalCount) = await _auditLogRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.UserId,
            null, // applicationId
            null, // actionType
            null, // action
            request.FromDate,
            request.ToDate,
            null, // isSuccess
            cancellationToken);

        var dtos = new List<AuditLogDto>();
        foreach (var log in logs)
        {
            var dto = new AuditLogDto
            {
                Id = log.Id,
                UserId = log.UserId,
                UserName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
                UserEmail = user.Email,
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

            // Get application name if available
            if (log.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdAsync(log.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug(
            "Retrieved {Count} audit logs for user {UserId} (page {Page})",
            dtos.Count, request.UserId, request.PageNumber);

        return new PagedAuditLogsDto
        {
            Logs = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
