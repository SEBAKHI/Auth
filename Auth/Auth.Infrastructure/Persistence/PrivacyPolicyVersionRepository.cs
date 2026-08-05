using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper repository for the privacy-policy revision registry and its
/// per-language documents.
/// </summary>
public class PrivacyPolicyVersionRepository : IPrivacyPolicyVersionRepository
{
    private const string VersionColumns = @"
        [Id], [Version], [EffectiveDateUtc], [IsPublished], [ChangeNote], [NotifiedAtUtc],
        [NotifiedCount], [CreatedAt], [CreatedBy]";

    private const string ArtifactColumns = @"
        [Id], [VersionId], [LanguageCode], [SourceLanguageCode], [Html],
        [ContentHash], [StyleHash], [DisclosureJson], [RenderedAt]";

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

        var rows = await connection.QueryAsync<VersionDto>($@"
            SELECT {VersionColumns}
            FROM [dbo].[PrivacyPolicyVersions]
            ORDER BY [Version] DESC");

        return rows.Select(r => r.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyVersion?> GetByVersionAsync(
        string version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<VersionDto>($@"
            SELECT {VersionColumns}
            FROM [dbo].[PrivacyPolicyVersions]
            WHERE [Version] = @Version",
            new { Version = version });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyVersion?> GetPublishedAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<VersionDto>($@"
            SELECT TOP 1 {VersionColumns}
            FROM [dbo].[PrivacyPolicyVersions]
            WHERE [IsPublished] = 1");

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
                ([Id], [Version], [EffectiveDateUtc], [IsPublished], [ChangeNote], [NotifiedAtUtc], [NotifiedCount], [CreatedAt], [CreatedBy])
            SELECT @Id, @Version, @EffectiveDateUtc, 0, @ChangeNote, NULL, NULL, @CreatedAt, @CreatedBy
            WHERE NOT EXISTS (
                SELECT 1 FROM [dbo].[PrivacyPolicyVersions] WHERE [Version] = @Version)",
            new
            {
                version.Id,
                version.Version,
                version.EffectiveDateUtc,
                version.ChangeNote,
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

    /// <inheritdoc />
    public async Task UpdateDetailsAsync(
        PrivacyPolicyVersion version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PrivacyPolicyVersions]
            SET [Version] = @Version,
                [EffectiveDateUtc] = @EffectiveDateUtc,
                [ChangeNote] = @ChangeNote
            WHERE [Id] = @Id",
            new { version.Id, version.Version, version.EffectiveDateUtc, version.ChangeNote });
    }

    /// <inheritdoc />
    public async Task PublishAsync(Guid versionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Single statement: exactly one row ends up published, with no window
        // in which none is (the public page would 404 during that gap).
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PrivacyPolicyVersions]
            SET [IsPublished] = CASE WHEN [Id] = @VersionId THEN 1 ELSE 0 END
            WHERE [IsPublished] = 1 OR [Id] = @VersionId",
            new { VersionId = versionId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrivacyPolicyTranslation>> GetTranslationsAsync(
        Guid versionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<TranslationDto>(@"
            SELECT [Id], [VersionId], [LanguageCode], [ContentJson],
                   [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[PrivacyPolicyTranslations]
            WHERE [VersionId] = @VersionId
            ORDER BY [LanguageCode]",
            new { VersionId = versionId });

        return rows.Select(r => r.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyTranslation?> GetTranslationAsync(
        Guid versionId, string languageCode, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<TranslationDto>(@"
            SELECT [Id], [VersionId], [LanguageCode], [ContentJson],
                   [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[PrivacyPolicyTranslations]
            WHERE [VersionId] = @VersionId AND [LanguageCode] = @LanguageCode",
            new { VersionId = versionId, LanguageCode = languageCode });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task UpsertTranslationAsync(
        PrivacyPolicyTranslation translation, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            MERGE [dbo].[PrivacyPolicyTranslations] WITH (HOLDLOCK) AS target
            USING (SELECT @VersionId AS [VersionId], @LanguageCode AS [LanguageCode]) AS source
                ON target.[VersionId] = source.[VersionId]
               AND target.[LanguageCode] = source.[LanguageCode]
            WHEN MATCHED THEN
                UPDATE SET [ContentJson] = @ContentJson,
                           [ModifiedAt] = @ModifiedAt,
                           [ModifiedBy] = @ModifiedBy
            WHEN NOT MATCHED THEN
                INSERT ([Id], [VersionId], [LanguageCode], [ContentJson], [CreatedAt], [CreatedBy])
                VALUES (@Id, @VersionId, @LanguageCode, @ContentJson, @CreatedAt, @CreatedBy);",
            new
            {
                translation.Id,
                translation.VersionId,
                translation.LanguageCode,
                translation.ContentJson,
                translation.CreatedAt,
                translation.CreatedBy,
                translation.ModifiedAt,
                translation.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task ReplaceArtifactsAsync(
        Guid versionId,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Delete-then-insert inside one transaction: readers either see the old
        // set or the new one, never a half-published mixture of two revisions.
        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[PrivacyPolicyArtifacts] WHERE [VersionId] = @VersionId",
            new { VersionId = versionId }, transaction);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PrivacyPolicyArtifacts]
                ([Id], [VersionId], [LanguageCode], [SourceLanguageCode], [Html],
                 [ContentHash], [StyleHash], [DisclosureJson], [RenderedAt])
            VALUES
                (@Id, @VersionId, @LanguageCode, @SourceLanguageCode, @Html,
                 @ContentHash, @StyleHash, @DisclosureJson, @RenderedAt)",
            artifacts.Select(a => new
            {
                a.Id,
                a.VersionId,
                a.LanguageCode,
                a.SourceLanguageCode,
                a.Html,
                a.ContentHash,
                a.StyleHash,
                a.DisclosureJson,
                a.RenderedAt
            }),
            transaction);

        transaction.Commit();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyArtifact?> GetArtifactAsync(
        Guid versionId, string languageCode, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ArtifactDto>($@"
            SELECT {ArtifactColumns}
            FROM [dbo].[PrivacyPolicyArtifacts]
            WHERE [VersionId] = @VersionId AND [LanguageCode] = @LanguageCode",
            new { VersionId = versionId, LanguageCode = languageCode });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<PrivacyPolicyArtifact?> GetPublishedArtifactAsync(
        string languageCode, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One join rather than "find the published version, then fetch its
        // document": this is the anonymous public path, so it gets one
        // round-trip and no window between the two reads.
        var row = await connection.QuerySingleOrDefaultAsync<ArtifactDto>(@"
            SELECT TOP 1
                a.[Id], a.[VersionId], a.[LanguageCode], a.[SourceLanguageCode],
                a.[Html], a.[ContentHash], a.[StyleHash], a.[DisclosureJson], a.[RenderedAt]
            FROM [dbo].[PrivacyPolicyArtifacts] a
            INNER JOIN [dbo].[PrivacyPolicyVersions] v ON v.[Id] = a.[VersionId]
            WHERE v.[IsPublished] = 1 AND a.[LanguageCode] = @LanguageCode",
            new { LanguageCode = languageCode });

        return row?.ToEntity();
    }

    // Internal DTOs for mapping from database
    private record VersionDto
    {
        public Guid Id { get; init; }
        public string Version { get; init; } = string.Empty;
        public DateTime EffectiveDateUtc { get; init; }
        public bool IsPublished { get; init; }
        public string? ChangeNote { get; init; }
        public DateTime? NotifiedAtUtc { get; init; }
        public int? NotifiedCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }

        public PrivacyPolicyVersion ToEntity() => new(
            Id, Version, EffectiveDateUtc, IsPublished, ChangeNote, NotifiedAtUtc,
            NotifiedCount, CreatedAt, CreatedBy);
    }

    private record TranslationDto
    {
        public Guid Id { get; init; }
        public Guid VersionId { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public string ContentJson { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public PrivacyPolicyTranslation ToEntity() => new(
            Id, VersionId, LanguageCode, ContentJson, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy);
    }

    private record ArtifactDto
    {
        public Guid Id { get; init; }
        public Guid VersionId { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public string SourceLanguageCode { get; init; } = string.Empty;
        public string Html { get; init; } = string.Empty;
        public string ContentHash { get; init; } = string.Empty;
        public string StyleHash { get; init; } = string.Empty;
        public string DisclosureJson { get; init; } = string.Empty;
        public DateTime RenderedAt { get; init; }

        public PrivacyPolicyArtifact ToEntity() => new(
            Id, VersionId, LanguageCode, SourceLanguageCode, Html, ContentHash,
            StyleHash, DisclosureJson, RenderedAt);
    }
}
