using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the notification layout repository.
/// </summary>
public class NotificationLayoutRepository : INotificationLayoutRepository
{
    private const string SelectColumns = @"
        [Id], [ApplicationId], [Channel], [Name], [DraftContent], [DraftStringsJson],
        [PublishedContent], [PublishedStringsJson], [PublishedAt], [PublishedBy],
        [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]";

    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationLayoutRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationLayout>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<NotificationLayoutDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[NotificationLayouts]
            ORDER BY CASE WHEN [ApplicationId] IS NULL THEN 0 ELSE 1 END, [Name], [Id]");

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<NotificationLayout?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<NotificationLayoutDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[NotificationLayouts]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        Guid? applicationId,
        NotificationChannelType channel,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[NotificationLayouts]
                WHERE [Channel] = @Channel
                  AND ((@ApplicationId IS NULL AND [ApplicationId] IS NULL)
                       OR [ApplicationId] = @ApplicationId)
            ) THEN 1 ELSE 0 END",
            new { ApplicationId = applicationId, Channel = (byte)channel });
    }

    /// <inheritdoc />
    public async Task<NotificationLayout> CreateAsync(NotificationLayout layout, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[NotificationLayouts]
                ([Id], [ApplicationId], [Channel], [Name], [DraftContent], [DraftStringsJson],
                 [PublishedContent], [PublishedStringsJson], [PublishedAt], [PublishedBy],
                 [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy])
            VALUES
                (@Id, @ApplicationId, @Channel, @Name, @DraftContent, @DraftStringsJson,
                 @PublishedContent, @PublishedStringsJson, @PublishedAt, @PublishedBy,
                 @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)",
            ToParams(layout));

        return layout;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(NotificationLayout layout, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationLayouts]
            SET [Name] = @Name,
                [DraftContent] = @DraftContent,
                [DraftStringsJson] = @DraftStringsJson,
                [PublishedContent] = @PublishedContent,
                [PublishedStringsJson] = @PublishedStringsJson,
                [PublishedAt] = @PublishedAt,
                [PublishedBy] = @PublishedBy,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            ToParams(layout));
    }

    /// <inheritdoc />
    public async Task<bool> TryPublishAsync(
        NotificationLayout layout,
        DateTime expectedRevisionAt,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[NotificationLayouts]
            SET [Name] = @Name,
                [DraftContent] = @DraftContent,
                [DraftStringsJson] = @DraftStringsJson,
                [PublishedContent] = @PublishedContent,
                [PublishedStringsJson] = @PublishedStringsJson,
                [PublishedAt] = @PublishedAt,
                [PublishedBy] = @PublishedBy,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id
              AND (([ModifiedAt] = @ExpectedRevisionAt)
                   OR ([ModifiedAt] IS NULL AND [CreatedAt] = @ExpectedRevisionAt))",
            new
            {
                layout.Id,
                layout.Name,
                layout.DraftContent,
                layout.DraftStringsJson,
                layout.PublishedContent,
                layout.PublishedStringsJson,
                layout.PublishedAt,
                layout.PublishedBy,
                layout.ModifiedAt,
                layout.ModifiedBy,
                ExpectedRevisionAt = expectedRevisionAt
            },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<NotificationLayoutRenderSource?> GetPublishedAsync(
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<PublishedLayoutRow>(@"
            SELECT [Id], [ApplicationId], [PublishedContent], [PublishedStringsJson]
            FROM [dbo].[NotificationLayouts]
            WHERE [Channel] = @Channel
              AND ((@ApplicationId IS NULL AND [ApplicationId] IS NULL)
                   OR [ApplicationId] = @ApplicationId)
              AND [PublishedContent] IS NOT NULL",
            new { ApplicationId = applicationId, Channel = (byte)channel });

        return row is null
            ? null
            : new NotificationLayoutRenderSource(
                row.Id, row.ApplicationId, row.PublishedContent!, row.PublishedStringsJson ?? "{}");
    }

    private static object ToParams(NotificationLayout layout) => new
    {
        layout.Id,
        layout.ApplicationId,
        Channel = (byte)layout.Channel,
        layout.Name,
        layout.DraftContent,
        layout.DraftStringsJson,
        layout.PublishedContent,
        layout.PublishedStringsJson,
        layout.PublishedAt,
        layout.PublishedBy,
        layout.CreatedAt,
        layout.CreatedBy,
        layout.ModifiedAt,
        layout.ModifiedBy
    };

    private record PublishedLayoutRow
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public string? PublishedContent { get; init; }
        public string? PublishedStringsJson { get; init; }
    }

    private record NotificationLayoutDto
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public byte Channel { get; init; }
        public string Name { get; init; } = string.Empty;
        public string DraftContent { get; init; } = string.Empty;
        public string DraftStringsJson { get; init; } = "{}";
        public string? PublishedContent { get; init; }
        public string? PublishedStringsJson { get; init; }
        public DateTime? PublishedAt { get; init; }
        public Guid? PublishedBy { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public NotificationLayout ToEntity() => new(
            Id,
            ApplicationId,
            (NotificationChannelType)Channel,
            Name,
            DraftContent,
            DraftStringsJson,
            PublishedContent,
            PublishedStringsJson,
            PublishedAt,
            PublishedBy,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
