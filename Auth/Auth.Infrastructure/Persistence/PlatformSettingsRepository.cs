using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the platform settings repository.
/// </summary>
public class PlatformSettingsRepository : IPlatformSettingsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PlatformSettingsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings?> GetAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<PlatformSettingsDto>(@"
            SELECT [Id], [PlatformName], [LogoUrl], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[PlatformSettings]
            WHERE [Id] = @Id",
            new { Id = PlatformSettings.SingletonId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Upsert: the row is normally seeded at deploy time, but stay
        // resilient if the seed step has not run on this environment.
        await connection.ExecuteAsync(@"
            MERGE [dbo].[PlatformSettings] AS target
            USING (SELECT @Id AS [Id]) AS source
            ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [PlatformName] = @PlatformName,
                    [LogoUrl] = @LogoUrl,
                    [ModifiedAt] = @ModifiedAt,
                    [ModifiedBy] = @ModifiedBy
            WHEN NOT MATCHED THEN
                INSERT ([Id], [PlatformName], [LogoUrl], [ModifiedAt], [ModifiedBy])
                VALUES (@Id, @PlatformName, @LogoUrl, @ModifiedAt, @ModifiedBy);",
            new
            {
                settings.Id,
                settings.PlatformName,
                settings.LogoUrl,
                settings.ModifiedAt,
                settings.ModifiedBy
            });
    }

    private record PlatformSettingsDto
    {
        public Guid Id { get; init; }
        public string PlatformName { get; init; } = string.Empty;
        public string? LogoUrl { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public PlatformSettings ToEntity() => new(
            Id,
            PlatformName,
            LogoUrl,
            ModifiedAt,
            ModifiedBy);
    }
}
