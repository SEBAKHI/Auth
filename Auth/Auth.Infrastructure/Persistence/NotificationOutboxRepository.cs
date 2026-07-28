using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the notification outbox. Claiming uses a single
/// UPDATE ... OUTPUT with READPAST row locking, so concurrent claimers (or a
/// dispatcher racing a crashed predecessor's reclaim) never double-deliver.
/// </summary>
public class NotificationOutboxRepository : INotificationOutboxRepository
{
    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        ("typeCode", ["o.[NotificationTypeCode]"]),
        ("recipient", ["o.[Recipient]"]),
        ("languageCode", ["o.[LanguageCode]"]),
        ("status", ["o.[Status]"]),
        ("attemptCount", ["o.[AttemptCount]"]),
        ("nextAttemptAt", ["o.[NextAttemptAt]"]),
        ("sentAt", ["o.[SentAt]"]),
        ("createdAt", ["o.[CreatedAt]"]));

    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationOutboxRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(NotificationOutboxMessage message, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[NotificationOutbox]
                ([Id], [NotificationTypeCode], [Channel], [ApplicationId], [Recipient], [RecipientName],
                 [RecipientUserId], [LanguageCode], [TemplateId], [TemplateVersionId], [TemplateVersionNumber], [Subject],
                 [BodyHtml], [BodyText], [Status], [AttemptCount], [NextAttemptAt], [CreatedAt], [CreatedBy])
            VALUES
                (@Id, @NotificationTypeCode, @Channel, @ApplicationId, @Recipient, @RecipientName,
                 @RecipientUserId, @LanguageCode, @TemplateId, @TemplateVersionId, @TemplateVersionNumber, @Subject,
                 @BodyHtml, @BodyText, @Status, @AttemptCount, @NextAttemptAt, @CreatedAt, @CreatedBy)",
            new
            {
                message.Id,
                message.NotificationTypeCode,
                Channel = (byte)message.Channel,
                message.ApplicationId,
                message.Recipient,
                message.RecipientName,
                message.RecipientUserId,
                message.LanguageCode,
                message.TemplateId,
                message.TemplateVersionId,
                message.TemplateVersionNumber,
                message.Subject,
                message.BodyHtml,
                message.BodyText,
                Status = (byte)message.Status,
                message.AttemptCount,
                message.NextAttemptAt,
                message.CreatedAt,
                message.CreatedBy
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationOutboxMessage>> ClaimBatchAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<OutboxRow>(@"
            UPDATE TOP (@BatchSize) [dbo].[NotificationOutbox] WITH (ROWLOCK, READPAST)
            SET [Status] = 1, [ClaimedAt] = GETUTCDATE()
            OUTPUT inserted.*
            WHERE [Status] IN (0, 3) AND [NextAttemptAt] <= GETUTCDATE()",
            new { BatchSize = batchSize });

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(Guid id, bool redactBody, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Sensitive bodies (one-time codes / tokenized links) are overwritten
        // the moment delivery succeeds — the delivery log keeps the metadata,
        // never the secret.
        await connection.ExecuteAsync(redactBody
            ? @"
            UPDATE [dbo].[NotificationOutbox]
            SET [Status] = 2, [SentAt] = GETUTCDATE(), [LastError] = NULL,
                [BodyHtml] = @RedactedBody, [BodyText] = @RedactedBody
            WHERE [Id] = @Id"
            : @"
            UPDATE [dbo].[NotificationOutbox]
            SET [Status] = 2, [SentAt] = GETUTCDATE(), [LastError] = NULL
            WHERE [Id] = @Id",
            new { Id = id, RedactedBody = NotificationTypeCodes.RedactedBody });
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(
        Guid id,
        string error,
        DateTime nextAttemptAt,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement decides retry vs dead-letter from the incremented count.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationOutbox]
            SET [AttemptCount] = [AttemptCount] + 1,
                [LastError] = @Error,
                [NextAttemptAt] = @NextAttemptAt,
                [Status] = CASE WHEN [AttemptCount] + 1 >= @MaxAttempts THEN 4 ELSE 3 END,
                [ClaimedAt] = NULL
            WHERE [Id] = @Id",
            new { Id = id, Error = error, NextAttemptAt = nextAttemptAt, MaxAttempts = maxAttempts });
    }

    /// <inheritdoc />
    public async Task<int> ReclaimStaleAsync(DateTime claimedBefore, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationOutbox]
            SET [Status] = 0, [ClaimedAt] = NULL, [NextAttemptAt] = GETUTCDATE()
            WHERE [Status] = 1 AND [ClaimedAt] < @ClaimedBefore",
            new { ClaimedBefore = claimedBefore });
    }

    /// <inheritdoc />
    public async Task<int> DeleteSentOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Status 2 = Sent.
        return await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[NotificationOutbox]
            WHERE [Status] = 2 AND [SentAt] < @CutoffUtc",
            new { CutoffUtc = cutoffUtc });
    }

    /// <inheritdoc />
    public async Task<bool> HasDueWorkAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[NotificationOutbox]
                WHERE [Status] IN (0, 3) AND [NextAttemptAt] <= GETUTCDATE()
            ) THEN 1 ELSE 0 END");
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<NotificationOutboxListItem> Messages, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        NotificationDeliveryStatus? status,
        NotificationChannelType? channel,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string fromAndWhere = @"
            FROM [dbo].[NotificationOutbox] o
            LEFT JOIN [dbo].[Applications] a ON a.[Id] = o.[ApplicationId]
            WHERE (@Status IS NULL OR o.[Status] = @Status)
              AND (@Channel IS NULL OR o.[Channel] = @Channel)
              AND (@SearchTerm IS NULL
                   OR o.[Recipient] LIKE '%' + @SearchTerm + '%'
                   OR o.[NotificationTypeCode] LIKE '%' + @SearchTerm + '%'
                   OR o.[Subject] LIKE '%' + @SearchTerm + '%')";

        var parameters = new
        {
            Status = (byte?)status,
            Channel = (byte?)channel,
            SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim(),
            Offset = (pageNumber - 1) * pageSize,
            PageSize = pageSize
        };

        var totalCount = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) {fromAndWhere}", parameters);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "o.[CreatedAt] DESC", "o.[Id]");

        var items = await connection.QueryAsync<ListItemRow>($@"
            SELECT o.[Id], o.[NotificationTypeCode], o.[Channel], o.[ApplicationId],
                   a.[Name] AS ApplicationName, o.[Recipient], o.[RecipientName],
                   o.[RecipientUserId], o.[LanguageCode], o.[TemplateId], o.[TemplateVersionId],
                   o.[TemplateVersionNumber], o.[Subject], o.[Status], o.[AttemptCount], o.[NextAttemptAt],
                   o.[SentAt], o.[LastError], o.[CreatedAt], o.[CreatedBy]
            {fromAndWhere}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters);

        return (items.Select(row => row.ToReadModel()).ToList(), totalCount);
    }

    /// <inheritdoc />
    public async Task<NotificationOutboxMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<OutboxRow>(@"
            SELECT * FROM [dbo].[NotificationOutbox]
            WHERE [Id] = @Id",
            new { Id = id });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<NotificationOutboxStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Status values are the NotificationDeliveryStatus TINYINTs:
        // 0 Pending, 1 Processing, 2 Sent, 3 Retry, 4 Dead. Pending and
        // Processing are both "in flight" to an operator; Retry and Dead are
        // both "did not go out".
        return await connection.QuerySingleAsync<NotificationOutboxStats>(@"
            SELECT
                COUNT(1) AS [Total],
                SUM(CASE WHEN [Status] IN (0, 1) THEN 1 ELSE 0 END) AS [Pending],
                SUM(CASE WHEN [Status] = 2 THEN 1 ELSE 0 END) AS [Sent],
                SUM(CASE WHEN [Status] IN (3, 4) THEN 1 ELSE 0 END) AS [Failed],
                SUM(CASE WHEN [CreatedAt] >= DATEADD(HOUR, -24, GETUTCDATE()) THEN 1 ELSE 0 END) AS [Last24Hours]
            FROM [dbo].[NotificationOutbox]");
    }

    /// <inheritdoc />
    public async Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Only failed messages are retryable; Pending/Processing/Sent are left alone.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationOutbox]
            SET [Status] = 0, [NextAttemptAt] = GETUTCDATE(), [ClaimedAt] = NULL
            WHERE [Id] = @Id AND [Status] IN (3, 4)",
            new { Id = id });

        return affected > 0;
    }

    private record ListItemRow
    {
        public Guid Id { get; init; }
        public string NotificationTypeCode { get; init; } = string.Empty;
        public byte Channel { get; init; }
        public Guid? ApplicationId { get; init; }
        public string? ApplicationName { get; init; }
        public string Recipient { get; init; } = string.Empty;
        public string? RecipientName { get; init; }
        public Guid? RecipientUserId { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public Guid? TemplateId { get; init; }
        public Guid? TemplateVersionId { get; init; }
        public int? TemplateVersionNumber { get; init; }
        public string Subject { get; init; } = string.Empty;
        public byte Status { get; init; }
        public int AttemptCount { get; init; }
        public DateTime NextAttemptAt { get; init; }
        public DateTime? SentAt { get; init; }
        public string? LastError { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid? CreatedBy { get; init; }

        public NotificationOutboxListItem ToReadModel() => new(
            Id, NotificationTypeCode, Channel, ApplicationId, ApplicationName,
            Recipient, RecipientName, RecipientUserId, LanguageCode, TemplateId,
            TemplateVersionId, TemplateVersionNumber, Subject, Status, AttemptCount,
            NextAttemptAt, SentAt, LastError, CreatedAt, CreatedBy);
    }

    private record OutboxRow
    {
        public Guid Id { get; init; }
        public string NotificationTypeCode { get; init; } = string.Empty;
        public byte Channel { get; init; }
        public Guid? ApplicationId { get; init; }
        public string Recipient { get; init; } = string.Empty;
        public string? RecipientName { get; init; }
        public Guid? RecipientUserId { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public Guid? TemplateId { get; init; }
        public Guid? TemplateVersionId { get; init; }
        public int? TemplateVersionNumber { get; init; }
        public string Subject { get; init; } = string.Empty;
        public string BodyHtml { get; init; } = string.Empty;
        public string? BodyText { get; init; }
        public byte Status { get; init; }
        public int AttemptCount { get; init; }
        public DateTime NextAttemptAt { get; init; }
        public DateTime? ClaimedAt { get; init; }
        public DateTime? SentAt { get; init; }
        public string? LastError { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid? CreatedBy { get; init; }

        public NotificationOutboxMessage ToEntity() => new(
            Id,
            NotificationTypeCode,
            (NotificationChannelType)Channel,
            ApplicationId,
            Recipient,
            RecipientName,
            RecipientUserId,
            LanguageCode,
            TemplateId,
            TemplateVersionId,
            TemplateVersionNumber,
            Subject,
            BodyHtml,
            BodyText,
            (NotificationDeliveryStatus)Status,
            AttemptCount,
            NextAttemptAt,
            ClaimedAt,
            SentAt,
            LastError,
            CreatedAt,
            CreatedBy);
    }
}
