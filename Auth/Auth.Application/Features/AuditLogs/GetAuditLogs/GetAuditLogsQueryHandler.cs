using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogs;

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
            request.ParticipantId,
            request.ParticipantRole,
            request.ApplicationId,
            request.Action,
            request.ActionType,
            request.IsSuccess,
            request.FromDate,
            request.ToDate,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        // Enrich logs with user and application names, batching the user lookup
        // so a page of logs stays one round-trip instead of one per row. Subject
        // and actor are looked up together: they are usually the same person, and
        // when they are not, that difference is the most useful thing on the row.
        var actorIds = logs
            .SelectMany(log => new[] { log.UserId, log.PerformedBy })
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var actors = actorIds.Count == 0
            ? []
            : await _userRepository.GetByIdsAsync(actorIds, cancellationToken) ?? [];
        var actorsById = actors.ToDictionary(user => user.Id);

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository, logs.Select(log => log.ApplicationId), cancellationToken);

        var dtos = new List<AuditLogDto>();
        foreach (var log in logs)
        {
            var dto = new AuditLogDto
            {
                Id = log.Id,
                UserId = log.UserId,
                PerformedBy = log.PerformedBy,
                SessionId = log.SessionId,
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

            if (log.UserId.HasValue && actorsById.TryGetValue(log.UserId.Value, out var user))
            {
                dto.UserName = NameLookupHelper.DisplayName(user);
                dto.UserEmail = user.Email;
            }

            if (log.PerformedBy.HasValue && actorsById.TryGetValue(log.PerformedBy.Value, out var actor))
            {
                dto.PerformedByName = NameLookupHelper.DisplayName(actor);
                dto.PerformedByEmail = actor.Email;
            }

            if (log.ApplicationId.HasValue)
            {
                dto.ApplicationName = applicationNames.GetValueOrDefault(log.ApplicationId.Value);
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
