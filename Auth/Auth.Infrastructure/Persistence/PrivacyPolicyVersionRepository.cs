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
        [NotifiedCount], [CreatedAt], [CreatedBy], [FirstPublishedAtUtc]";

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
    public async Task<PrivacyPolicyVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<VersionDto>($@"
            SELECT {VersionColumns}
            FROM [dbo].[PrivacyPolicyVersions]
            WHERE [Id] = @Id",
            new { Id = id });

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> GetServedVersionIdsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var ids = await connection.QueryAsync<Guid>(
            "SELECT DISTINCT [VersionId] FROM [dbo].[PrivacyPolicyArtifacts]");

        return ids.ToHashSet();
    }

    /// <inheritdoc />
    public async Task<bool> IsCitedByDeletionRecordsAsync(string version, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            $"SELECT CAST(CASE WHEN {DeletionRecordsCite("@Version")} THEN 1 ELSE 0 END AS BIT)",
            new { Version = version });
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteDraftAsync(
        Guid id, string policyVersionInUse, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so one transaction: the written languages cascade with the row, and the
        // conditions are evaluated where the delete happens. HOLDLOCK on the served documents
        // keeps a concurrent publish from slipping its documents in between this check and the
        // delete; a publish that comes after finds the row gone and fails on its foreign key. So
        // the revision ends up either published with its row or deleted, never half of each.
        var affected = await connection.ExecuteAsync(new CommandDefinition($@"
            DELETE v
            FROM [dbo].[PrivacyPolicyVersions] v
            WHERE v.[Id] = @Id
              AND v.[IsPublished] = 0
              AND v.[FirstPublishedAtUtc] IS NULL
              AND v.[NotifiedAtUtc] IS NULL
              AND v.[Version] <> @PolicyVersionInUse
              AND NOT EXISTS (
                  SELECT 1 FROM [dbo].[PrivacyPolicyArtifacts] a WITH (UPDLOCK, HOLDLOCK)
                  WHERE a.[VersionId] = v.[Id])
              AND NOT ({DeletionRecordsCite("v.[Version]")})",
            new { Id = id, PolicyVersionInUse = policyVersionInUse },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    /// <summary>
    /// A predicate: whether any deletion request or tombstone carries the version. Deletion
    /// records are stamped with the configured version when they are made, so a version they
    /// cite was in force once, even if no served document of it remains (revisions published
    /// before served documents were stored have none).
    /// </summary>
    private static string DeletionRecordsCite(string version) => $@"
        (EXISTS (SELECT 1 FROM [dbo].[AccountDeletionRequests] r WHERE r.[PolicyVersion] = {version})
         OR EXISTS (SELECT 1 FROM [dbo].[AccountDeletionTombstones] t WHERE t.[PolicyVersion] = {version}))";

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
    public async Task PublishArtifactsAsync(
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

        // The artifact set and the published pointer are one fact. Committing
        // them together prevents the public version from ever referring to a
        // half-written or stale set of language documents. The first publish
        // also stamps FirstPublishedAtUtc, which a supersede never clears: it is
        // what keeps a former policy from reading as a deletable draft.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PrivacyPolicyVersions]
            SET [IsPublished] = CASE WHEN [Id] = @VersionId THEN 1 ELSE 0 END,
                [FirstPublishedAtUtc] = CASE WHEN [Id] = @VersionId
                    THEN COALESCE([FirstPublishedAtUtc], SYSUTCDATETIME())
                    ELSE [FirstPublishedAtUtc] END
            WHERE [IsPublished] = 1 OR [Id] = @VersionId",
            new { VersionId = versionId }, transaction);

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
        public DateTime? FirstPublishedAtUtc { get; init; }

        public PrivacyPolicyVersion ToEntity() => new(
            Id, Version, EffectiveDateUtc, IsPublished, ChangeNote, NotifiedAtUtc,
            NotifiedCount, CreatedAt, CreatedBy, FirstPublishedAtUtc);
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
