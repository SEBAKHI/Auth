using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Handler for getting a paginated list of audit logs.
/// </summary>
public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, ErrorOr<PagedAuditLogsDto>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetAuditLogsQueryHandler> _logger;

    public GetAuditLogsQueryHandler(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetAuditLogsQueryHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedAuditLogsDto>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var (logs, totalCount) = await _auditLogRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.UserId,
            request.ApplicationId,
            request.ActionType,
            request.Action,
            request.FromDate,
            request.ToDate,
            request.IsSuccess,
            cancellationToken);

        // Enrich logs with user and application names
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
                var app = await _applicationRepository.GetByIdAsync(log.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug(
            "Retrieved {Count} audit logs (page {Page} of {TotalPages})",
            dtos.Count, request.PageNumber, (int)Math.Ceiling(totalCount / (double)request.PageSize));

        return new PagedAuditLogsDto
        {
            Logs = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
