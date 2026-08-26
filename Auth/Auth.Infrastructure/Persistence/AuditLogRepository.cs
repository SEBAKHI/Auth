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

        // PerformedBy used to be written as auditLog.UserId, which made it
        // impossible for the actor to differ from the subject no matter what the
        // caller passed — so "who locked this account" had no answer the trail
        // could give. It is its own field now, and SessionId is written too: the
        // column and its index have always been there and nothing ever filled
        // them.
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[AuditLogs] (
                [Id], [UserId], [ApplicationId], [SessionId], [Action], [EntityType], [EntityId],
                [OldValues], [NewValues], [IpAddress], [UserAgent], [Details],
                [Timestamp], [PerformedBy], [ActionType], [IsSuccess], [ErrorMessage], [CorrelationId]
            ) VALUES (
                @Id, @UserId, @ApplicationId, @SessionId, @Action, @EntityType, @EntityId,
                @OldValues, @NewValues, @IpAddress, @UserAgent, @Details,
                @Timestamp, @PerformedBy, @ActionType, @IsSuccess, @ErrorMessage, @CorrelationId
            )",
            new
            {
                auditLog.Id,
                auditLog.UserId,
                auditLog.ApplicationId,
                auditLog.SessionId,
                auditLog.Action,
                auditLog.EntityType,
                auditLog.EntityId,
                auditLog.OldValues,
                auditLog.NewValues,
                auditLog.IpAddress,
                auditLog.UserAgent,
                Details = auditLog.AdditionalData,
                auditLog.Timestamp,
                auditLog.PerformedBy,
                auditLog.ActionType,
                auditLog.IsSuccess,
                auditLog.ErrorMessage,
                auditLog.CorrelationId
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

    /// <summary>
    /// The joins every read of this table shares. Two separate joins onto Users,
    /// because an audit row names two different people and they are only the same
    /// person when someone acted on their own account.
    /// </summary>
    /// <remarks>
    /// All LEFT: a system action has neither, and an inner join would delete the
    /// retention sweep and the policy publications from every page.
    /// </remarks>
    private const string Joins = @"
            LEFT JOIN [dbo].[Users] u ON a.[UserId] = u.[Id]
            LEFT JOIN [dbo].[Users] pb ON a.[PerformedBy] = pb.[Id]
            LEFT JOIN [dbo].[Applications] ap ON a.[ApplicationId] = ap.[Id]";

    // Actor sorts on the PERFORMER's row (pb), subject on the row the action was
    // done to (u). Before those were two entries, "actor" ordered on u — so a
    // page sorted by actor was ordered by the people acted upon, and an
    // administrator asking "what did this operator do" got a list keyed to
    // everyone but them.
    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.AuditLogs.Action, ["a.[Action]"]),
        (SortFields.AuditLogs.ActionType, ["a.[ActionType]"]),
        (SortFields.AuditLogs.EntityType, ["a.[EntityType]"]),
        (SortFields.AuditLogs.Timestamp, ["a.[Timestamp]"]),
        (SortFields.AuditLogs.IpAddress, ["a.[IpAddress]"]),
        (SortFields.AuditLogs.UserAgent, ["a.[UserAgent]"]),
        (SortFields.AuditLogs.Actor, ["COALESCE(pb.[Email], pb.[FullName])"]),
        (SortFields.AuditLogs.Subject, ["COALESCE(u.[Email], u.[FullName])"]),
        (SortFields.AuditLogs.UserName, ["u.[FullName]"]),
        (SortFields.AuditLogs.UserEmail, ["u.[Email]"]),
        (SortFields.AuditLogs.ApplicationName, ["ap.[Name]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? participantId,
        AuditParticipantRole? participantRole,
        Guid? applicationId,
        string? action,
        string? actionType,
        bool? isSuccess,
        DateTime? fromDate,
        DateTime? toDate,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Both of these were accepted and then silently dropped for as long as
        // the columns did not exist, so a filtered request came back unfiltered
        // with nothing to say so. They filter for real now.
        var whereClause = new StringBuilder("WHERE 1=1");
        var parameters = new DynamicParameters();

        if (participantId.HasValue)
        {
            // Three questions, three shapes — and deliberately NOT
            // "a.[UserId] = @P OR a.[PerformedBy] = @P". That predicate is
            // sargable against neither IX_AuditLogs_UserId nor
            // IX_AuditLogs_PerformedBy, so it scans a table whose retention
            // floor is 1095 days, once for the page and again for the count
            // below. Each branch here keeps its own index seek.
            switch (participantRole ?? AuditParticipantRole.Subject)
            {
                case AuditParticipantRole.Actor:
                    whereClause.Append(" AND a.[PerformedBy] = @ParticipantId");
                    break;

                case AuditParticipantRole.Either:
                    // A semi-join over two seeks. UNION and never UNION ALL: a
                    // self-action — a sign-in, a password change — has
                    // UserId = PerformedBy and satisfies both branches, and the
                    // duplicate would be counted twice by the COUNT below, so
                    // the pager would advertise more rows than it can show.
                    whereClause.Append(@" AND a.[Id] IN (
                SELECT [Id] FROM [dbo].[AuditLogs] WHERE [UserId] = @ParticipantId
                UNION
                SELECT [Id] FROM [dbo].[AuditLogs] WHERE [PerformedBy] = @ParticipantId)");
                    break;

                default:
                    whereClause.Append(" AND a.[UserId] = @ParticipantId");
                    break;
            }

            parameters.Add("ParticipantId", participantId.Value);
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

        if (!string.IsNullOrEmpty(actionType))
        {
            whereClause.Append(" AND a.[ActionType] = @ActionType");
            parameters.Add("ActionType", actionType);
        }

        if (isSuccess.HasValue)
        {
            // Deliberately an equality test rather than "not the other one".
            // Rows written before the column existed are NULL, and NULL is not a
            // failure and not a success — it is the absence of a record. Neither
            // filter should claim them.
            whereClause.Append(" AND a.[IsSuccess] = @IsSuccess");
            parameters.Add("IsSuccess", isSuccess.Value);
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
            SELECT a.* FROM [dbo].[AuditLogs] a{Joins}
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
            SELECT a.* FROM [dbo].[AuditLogs] a{Joins}
            WHERE a.[EntityType] = @EntityType AND a.[EntityId] = @EntityId
            ORDER BY {orderBy}",
            new { EntityType = entityType, EntityId = entityId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
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
        public string? ActionType { get; init; }
        public bool? IsSuccess { get; init; }
        public string? ErrorMessage { get; init; }
        public string? CorrelationId { get; init; }

        // Every field here is now read from the row. Four of them used to be
        // invented on the way out — ActionType as the literal "System",
        // IsSuccess as true, the other two as null — because the columns did not
        // exist. That made the audit screen report every event as a success, and
        // it reported it about rows nobody had ever asked the outcome of.
        // ActionType falls back to empty rather than to a word that reads like a
        // category, so a row written before the column is visibly blank instead
        // of quietly mislabelled.
        public AuditLog ToEntity() => new(
            Id,
            UserId,
            ApplicationId,
            ActionType ?? string.Empty,
            Action,
            EntityType,
            EntityId,
            OldValues,
            NewValues,
            IpAddress,
            UserAgent,
            Details,
            IsSuccess,
            ErrorMessage,
            Timestamp,
            CorrelationId,
            PerformedBy,
            SessionId);
    }
}
