using System.Text;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth_Lib.Infrastructure.Data;

/// <summary>
/// Dapper implementation of the audit log repository.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AuditLogRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task CreateAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[AuditLogs] (
                [Id], [UserId], [ApplicationId], [Action], [EntityType], [EntityId],
                [OldValues], [NewValues], [IpAddress], [UserAgent], [Details],
                [Timestamp], [PerformedBy]
            ) VALUES (
                @Id, @UserId, @ApplicationId, @Action, @EntityType, @EntityId,
                @OldValues, @NewValues, @IpAddress, @UserAgent, @Details,
                @Timestamp, @PerformedBy
            )",
            new
            {
                auditLog.Id,
                auditLog.UserId,
                auditLog.ApplicationId,
                auditLog.Action,
                auditLog.EntityType,
                auditLog.EntityId,
                auditLog.OldValues,
                auditLog.NewValues,
                auditLog.IpAddress,
                auditLog.UserAgent,
                Details = auditLog.AdditionalData,
                auditLog.Timestamp,
                PerformedBy = auditLog.UserId
            });
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? userId = null,
        Guid? applicationId = null,
        string? actionType = null,
        string? action = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        bool? isSuccess = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var whereClause = new StringBuilder("WHERE 1=1");
        var parameters = new DynamicParameters();

        if (userId.HasValue)
        {
            whereClause.Append(" AND [UserId] = @UserId");
            parameters.Add("UserId", userId.Value);
        }

        if (applicationId.HasValue)
        {
            whereClause.Append(" AND [ApplicationId] = @ApplicationId");
            parameters.Add("ApplicationId", applicationId.Value);
        }

        if (!string.IsNullOrEmpty(action))
        {
            whereClause.Append(" AND [Action] = @Action");
            parameters.Add("Action", action);
        }

        if (fromDate.HasValue)
        {
            whereClause.Append(" AND [Timestamp] >= @FromDate");
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause.Append(" AND [Timestamp] <= @ToDate");
            parameters.Add("ToDate", toDate.Value);
        }

        // Get total count
        var countSql = $"SELECT COUNT(1) FROM [dbo].[AuditLogs] {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get paged results
        var offset = (pageNumber - 1) * pageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var sql = $@"
            SELECT * FROM [dbo].[AuditLogs]
            {whereClause}
            ORDER BY [Timestamp] DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var dtos = await connection.QueryAsync<AuditLogDto>(sql, parameters);
        var logs = dtos.Select(dto => dto.ToEntity()).ToList();

        return (logs, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<AuditLogDto>(@"
            SELECT * FROM [dbo].[AuditLogs]
            WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId
            ORDER BY [Timestamp] DESC",
            new { EntityType = entityType, EntityId = entityId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Note: The current AuditLogs table may not have a CorrelationId column
        // This is a placeholder implementation
        return [];
    }

    /// <inheritdoc />
    public async Task CleanupOldLogsAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[AuditLogs] WHERE [Timestamp] < @OlderThan",
            new { OlderThan = olderThan });
    }

    private record AuditLogDto
    {
        public Guid Id { get; init; }
        public Guid? UserId { get; init; }
        public Guid? ApplicationId { get; init; }
        public Guid? SessionId { get; init; }
        public string Action { get; init; } = string.Empty;
        public string? EntityType { get; init; }
        public Guid? EntityId { get; init; }
        public string? OldValues { get; init; }
        public string? NewValues { get; init; }
        public string? IpAddress { get; init; }
        public string? UserAgent { get; init; }
        public string? Details { get; init; }
        public DateTime Timestamp { get; init; }
        public Guid? PerformedBy { get; init; }

        public AuditLog ToEntity() => new(
            Id,
            UserId,
            ApplicationId,
            "System",  // ActionType - not in current DB schema
            Action,
            EntityType,
            EntityId,
            OldValues,
            NewValues,
            IpAddress,
            UserAgent,
            Details,
            true,  // IsSuccess - not in current DB schema
            null,  // ErrorMessage - not in current DB schema
            Timestamp,
            null); // CorrelationId - not in current DB schema
    }
}
