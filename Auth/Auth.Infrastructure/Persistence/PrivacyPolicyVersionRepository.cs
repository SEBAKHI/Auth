using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper repository for the privacy-policy revision registry.
/// </summary>
public class PrivacyPolicyVersionRepository : IPrivacyPolicyVersionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PrivacyPolicyVersionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrivacyPolicyVersion>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<VersionDto>(@"
            SELECT [Id], [Version], [EffectiveDateUtc], [NotifiedAtUtc], [NotifiedCount],
                   [CreatedAt], [CreatedBy]
            FROM [dbo].[PrivacyPolicyVersions]
            ORDER BY [Version] DESC");

        return rows.Select(r => r.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyVersion?> GetByVersionAsync(
        string version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<VersionDto>(@"
            SELECT [Id], [Version], [EffectiveDateUtc], [NotifiedAtUtc], [NotifiedCount],
                   [CreatedAt], [CreatedBy]
            FROM [dbo].[PrivacyPolicyVersions]
            WHERE [Version] = @Version",
            new { Version = version });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        PrivacyPolicyVersion version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // WHERE NOT EXISTS + rowcount: the unique index arbitrates the race
        // without surfacing a duplicate-key exception.
        var inserted = await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PrivacyPolicyVersions]
                ([Id], [Version], [EffectiveDateUtc], [NotifiedAtUtc], [NotifiedCount], [CreatedAt], [CreatedBy])
            SELECT @Id, @Version, @EffectiveDateUtc, NULL, NULL, @CreatedAt, @CreatedBy
            WHERE NOT EXISTS (
                SELECT 1 FROM [dbo].[PrivacyPolicyVersions] WHERE [Version] = @Version)",
            new
            {
                version.Id,
                version.Version,
                version.EffectiveDateUtc,
                version.CreatedAt,
                version.CreatedBy
            });

        return inserted > 0;
    }

    /// <inheritdoc />
    public async Task UpdateNotifiedAsync(
        PrivacyPolicyVersion version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PrivacyPolicyVersions]
            SET [NotifiedAtUtc] = @NotifiedAtUtc, [NotifiedCount] = @NotifiedCount
            WHERE [Id] = @Id",
            new { version.Id, version.NotifiedAtUtc, version.NotifiedCount });
    }

    // Internal DTO for mapping from database
    private record VersionDto
    {
        public Guid Id { get; init; }
        public string Version { get; init; } = string.Empty;
        public DateTime EffectiveDateUtc { get; init; }
        public DateTime? NotifiedAtUtc { get; init; }
        public int? NotifiedCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }

        public PrivacyPolicyVersion ToEntity() => new(
            Id, Version, EffectiveDateUtc, NotifiedAtUtc, NotifiedCount, CreatedAt, CreatedBy);
    }
}
