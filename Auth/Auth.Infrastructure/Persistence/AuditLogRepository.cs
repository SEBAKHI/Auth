using System.Text;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

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
    public async Task CreateAsync(AuditLog auditLog, CancellationToken cancellationToken)
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
    public async Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuditLogDto>(@"
            SELECT * FROM [dbo].[AuditLogs]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    // Actor/user/application fields sort on the joined Users and Applications
    // rows (1:1 by primary key, LEFT so system events with null FKs stay
    // included); actor mirrors the UI's `userEmail ?? userName` display value.
    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.AuditLogs.Action, ["a.[Action]"]),
        (SortFields.AuditLogs.EntityType, ["a.[EntityType]"]),
        (SortFields.AuditLogs.Timestamp, ["a.[Timestamp]"]),
        (SortFields.AuditLogs.IpAddress, ["a.[IpAddress]"]),
        (SortFields.AuditLogs.UserAgent, ["a.[UserAgent]"]),
        (SortFields.AuditLogs.Actor, ["COALESCE(u.[Email], u.[FullName])"]),
        (SortFields.AuditLogs.UserName, ["u.[FullName]"]),
        (SortFields.AuditLogs.UserEmail, ["u.[Email]"]),
        (SortFields.AuditLogs.ApplicationName, ["ap.[Name]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? userId,
        Guid? applicationId,
        string? actionType,
        string? action,
        DateTime? fromDate,
        DateTime? toDate,
        bool? isSuccess,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var whereClause = new StringBuilder("WHERE 1=1");
        var parameters = new DynamicParameters();

        if (userId.HasValue)
        {
            whereClause.Append(" AND a.[UserId] = @UserId");
            parameters.Add("UserId", userId.Value);
        }

        if (applicationId.HasValue)
        {
            whereClause.Append(" AND a.[ApplicationId] = @ApplicationId");
            parameters.Add("ApplicationId", applicationId.Value);
        }

        if (!string.IsNullOrEmpty(action))
        {
            whereClause.Append(" AND a.[Action] LIKE @Action");
            parameters.Add("Action", $"%{action}%");
        }

        if (fromDate.HasValue)
        {
            whereClause.Append(" AND a.[Timestamp] >= @FromDate");
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause.Append(" AND a.[Timestamp] <= @ToDate");
            parameters.Add("ToDate", toDate.Value);
        }

        // Get total count
        var countSql = $"SELECT COUNT(1) FROM [dbo].[AuditLogs] a {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get paged results
        var offset = (pageNumber - 1) * pageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var orderBy = SortSql.OrderBy(
            SortColumns, sortBy, sortDirection, "a.[Timestamp] DESC", "a.[Id]");
        var sql = $@"
            SELECT a.* FROM [dbo].[AuditLogs] a
            LEFT JOIN [dbo].[Users] u ON a.[UserId] = u.[Id]
            LEFT JOIN [dbo].[Applications] ap ON a.[ApplicationId] = ap.[Id]
            {whereClause}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var dtos = await connection.QueryAsync<AuditLogDto>(sql, parameters);
        var logs = dtos.Select(dto => dto.ToEntity()).ToList();

        return (logs, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(
            SortColumns, sortBy, sortDirection, "a.[Timestamp] DESC", "a.[Id]");
        var dtos = await connection.QueryAsync<AuditLogDto>($@"
            SELECT a.* FROM [dbo].[AuditLogs] a
            LEFT JOIN [dbo].[Users] u ON a.[UserId] = u.[Id]
            LEFT JOIN [dbo].[Applications] ap ON a.[ApplicationId] = ap.[Id]
            WHERE a.[EntityType] = @EntityType AND a.[EntityId] = @EntityId
            ORDER BY {orderBy}",
            new { EntityType = entityType, EntityId = entityId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Note: The current AuditLogs table may not have a CorrelationId column
        // This is a placeholder implementation
        return [];
    }

    /// <inheritdoc />
    public async Task CleanupOldLogsAsync(DateTime olderThan, CancellationToken cancellationToken)
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
