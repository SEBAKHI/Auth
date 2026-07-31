using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the system-settings override repository with
/// rowversion-based optimistic concurrency.
/// </summary>
public class SystemSettingsRepository : ISystemSettingsRepository
{
    private const string SelectColumns =
        "[SectionKey], [OverridesJson], [Version], [ModifiedAt], [ModifiedBy], [RowVersion]";

    private readonly IDbConnectionFactory _connectionFactory;

    public SystemSettingsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemSettingsOverride>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<SystemSettingsOverrideDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[SystemSettingsOverrides]");

        return rows.Select(r => r.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<SystemSettingsOverride?> GetAsync(string sectionKey, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SystemSettingsOverrideDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[SystemSettingsOverrides]
            WHERE [SectionKey] = @SectionKey",
            new { SectionKey = sectionKey });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<SystemSettingsUpsertResult> UpsertAsync(
        SystemSettingsOverride settings,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Concurrency contract: expectedRowVersion null means "the client
        // believes no row exists" (insert-only); otherwise the stored
        // rowversion must still match (update-only). Anything else is a
        // conflict, reported as Success = 0 without writing.
        var row = await connection.QueryFirstAsync<UpsertResultDto>(@"
            IF @ExpectedRowVersion IS NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM [dbo].[SystemSettingsOverrides] WHERE [SectionKey] = @SectionKey)
                    SELECT CAST(0 AS BIT) AS [Success], CAST(NULL AS VARBINARY(8)) AS [RowVersion], CAST(NULL AS INT) AS [Version];
                ELSE
                BEGIN
                    INSERT INTO [dbo].[SystemSettingsOverrides] ([SectionKey], [OverridesJson], [Version], [ModifiedAt], [ModifiedBy])
                    VALUES (@SectionKey, @OverridesJson, 1, @ModifiedAt, @ModifiedBy);

                    SELECT CAST(1 AS BIT) AS [Success], [RowVersion], [Version]
                    FROM [dbo].[SystemSettingsOverrides]
                    WHERE [SectionKey] = @SectionKey;
                END
            END
            ELSE
            BEGIN
                UPDATE [dbo].[SystemSettingsOverrides]
                SET [OverridesJson] = @OverridesJson,
                    [Version] = [Version] + 1,
                    [ModifiedAt] = @ModifiedAt,
                    [ModifiedBy] = @ModifiedBy
                WHERE [SectionKey] = @SectionKey AND [RowVersion] = @ExpectedRowVersion;

                IF @@ROWCOUNT = 0
                    SELECT CAST(0 AS BIT) AS [Success], CAST(NULL AS VARBINARY(8)) AS [RowVersion], CAST(NULL AS INT) AS [Version];
                ELSE
                    SELECT CAST(1 AS BIT) AS [Success], [RowVersion], [Version]
                    FROM [dbo].[SystemSettingsOverrides]
                    WHERE [SectionKey] = @SectionKey;
            END",
            new
            {
                settings.SectionKey,
                settings.OverridesJson,
                settings.ModifiedAt,
                settings.ModifiedBy,
                ExpectedRowVersion = expectedRowVersion
            });

        return new SystemSettingsUpsertResult(row.Success, row.RowVersion, row.Version);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string sectionKey, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[SystemSettingsOverrides]
            WHERE [SectionKey] = @SectionKey",
            new { SectionKey = sectionKey });

        return affected > 0;
    }

    private record SystemSettingsOverrideDto
    {
        public string SectionKey { get; init; } = string.Empty;
        public string OverridesJson { get; init; } = "{}";
        public int Version { get; init; }
        public DateTime ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }
        public byte[]? RowVersion { get; init; }

        public SystemSettingsOverride ToEntity() => new(
            SectionKey,
            OverridesJson,
            Version,
            ModifiedAt,
            ModifiedBy,
            RowVersion);
    }

    private record UpsertResultDto
    {
        public bool Success { get; init; }
        public byte[]? RowVersion { get; init; }
        public int? Version { get; init; }
    }
}
