using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the notification type repository.
/// </summary>
public class NotificationTypeRepository : INotificationTypeRepository
{
    private const string SelectColumns = @"
        [Id], [Code], [Name], [Description], [IsSystem], [VariablesJson],
        [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]";

    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationTypeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationType>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<NotificationTypeDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[NotificationTypes]
            ORDER BY [Name], [Id]");

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<NotificationType?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<NotificationTypeDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[NotificationTypes]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<NotificationType?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<NotificationTypeDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[NotificationTypes]
            WHERE [Code] = @Code",
            new { Code = code });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(NotificationType type, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationTypes]
            SET [Name] = @Name,
                [Description] = @Description,
                [VariablesJson] = @VariablesJson,
                [SampleDataJson] = @SampleDataJson,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                type.Id,
                type.Name,
                type.Description,
                type.VariablesJson,
                type.SampleDataJson,
                type.ModifiedAt,
                type.ModifiedBy
            });
    }

    private record NotificationTypeDto
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsSystem { get; init; }
        public string VariablesJson { get; init; } = "[]";
        public string SampleDataJson { get; init; } = "{}";
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public NotificationType ToEntity() => new(
            Id,
            Code,
            Name,
            Description,
            IsSystem,
            VariablesJson,
            SampleDataJson,
            IsActive,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
