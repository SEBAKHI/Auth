using System.Data;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the notification template repository. Aggregate
/// writes (template + versions + translations + pointer swaps) run in a single
/// transaction; the send path reads only published content.
/// </summary>
public class NotificationTemplateRepository : INotificationTemplateRepository
{
    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        ("typeName", ["nt.[Name]"]),
        ("typeCode", ["nt.[Code]"]),
        ("applicationName", ["a.[Name]"]),
        ("channel", ["t.[Channel]"]),
        ("defaultLanguage", ["t.[DefaultLanguage]"]),
        ("publishedVersionNumber", ["pv.[VersionNumber]"]),
        ("createdAt", ["t.[CreatedAt]"]),
        ("modifiedAt", ["t.[ModifiedAt]"]));

    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationTemplateRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    #region Admin CRUD (full aggregate)

    /// <inheritdoc />
    public async Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var templateDto = await connection.QueryFirstOrDefaultAsync<TemplateDto>(@"
            SELECT [Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage],
                   [PublishedVersionId], [DraftVersionId], [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[NotificationTemplates]
            WHERE [Id] = @Id",
            new { Id = id });

        if (templateDto is null)
        {
            return null;
        }

        var versionDtos = (await connection.QueryAsync<VersionDto>(@"
            SELECT [Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy]
            FROM [dbo].[NotificationTemplateVersions]
            WHERE [TemplateId] = @Id
            ORDER BY [VersionNumber]",
            new { Id = id })).ToList();

        var translationDtos = (await connection.QueryAsync<TranslationDto>(@"
            SELECT tr.[Id], tr.[VersionId], tr.[LanguageCode], tr.[Subject], tr.[BodyHtml],
                   tr.[BodyText], tr.[ModifiedAt], tr.[ModifiedBy]
            FROM [dbo].[NotificationTemplateTranslations] tr
            INNER JOIN [dbo].[NotificationTemplateVersions] v ON v.[Id] = tr.[VersionId]
            WHERE v.[TemplateId] = @Id",
            new { Id = id })).ToList();

        var translationsByVersion = translationDtos
            .GroupBy(t => t.VersionId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.ToEntity()).ToList());

        var versions = versionDtos
            .Select(v => v.ToEntity(
                translationsByVersion.TryGetValue(v.Id, out var translations)
                    ? translations
                    : []))
            .ToList();

        return templateDto.ToEntity(versions);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        Guid notificationTypeId,
        Guid? applicationId,
        NotificationChannelType channel,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[NotificationTemplates]
                WHERE [NotificationTypeId] = @NotificationTypeId
                  AND [Channel] = @Channel
                  AND ((@ApplicationId IS NULL AND [ApplicationId] IS NULL)
                       OR [ApplicationId] = @ApplicationId)
            ) THEN 1 ELSE 0 END",
            new { NotificationTypeId = notificationTypeId, ApplicationId = applicationId, Channel = (byte)channel });
    }

    /// <inheritdoc />
    public async Task<NotificationTemplate> CreateAsync(
        NotificationTemplate template,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Insert the template with null pointers first (versions reference the
        // template, and the pointers reference versions).
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[NotificationTemplates]
                ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage],
                 [PublishedVersionId], [DraftVersionId], [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy])
            VALUES
                (@Id, @NotificationTypeId, @ApplicationId, @Channel, @DefaultLanguage,
                 NULL, NULL, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)",
            new
            {
                template.Id,
                template.NotificationTypeId,
                template.ApplicationId,
                Channel = (byte)template.Channel,
                template.DefaultLanguage,
                template.CreatedAt,
                template.CreatedBy,
                template.ModifiedAt,
                template.ModifiedBy
            },
            transaction);

        foreach (var version in template.Versions)
        {
            await InsertVersionAsync(connection, transaction, version);
        }

        await UpdatePointersAndAuditAsync(connection, transaction, template);

        transaction.Commit();
        return template;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(NotificationTemplate template, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existingVersionIds = (await connection.QueryAsync<Guid>(@"
            SELECT [Id] FROM [dbo].[NotificationTemplateVersions]
            WHERE [TemplateId] = @Id",
            new { template.Id },
            transaction)).ToHashSet();

        // 1. Insert versions that are new to the aggregate (with their translations).
        foreach (var version in template.Versions.Where(v => !existingVersionIds.Contains(v.Id)))
        {
            await InsertVersionAsync(connection, transaction, version);
        }

        // 2. Sync the mutable draft version: change note + translation upserts/deletes.
        //    Published and historical versions are immutable by design.
        if (template.DraftVersion is { } draft && existingVersionIds.Contains(draft.Id))
        {
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[NotificationTemplateVersions]
                SET [ChangeNote] = @ChangeNote
                WHERE [Id] = @Id",
                new { draft.Id, draft.ChangeNote },
                transaction);

            await SyncDraftTranslationsAsync(connection, transaction, draft);
        }
        else if (template.DraftVersion is { } newDraft)
        {
            // Draft was inserted in step 1; only the change note may have been set after.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[NotificationTemplateVersions]
                SET [ChangeNote] = @ChangeNote
                WHERE [Id] = @Id",
                new { newDraft.Id, newDraft.ChangeNote },
                transaction);
        }

        // 3. Move the pointers (after any new version exists, before deleting orphans).
        await UpdatePointersAndAuditAsync(connection, transaction, template);

        // 4. Remove versions discarded from the aggregate (translations cascade).
        var aggregateVersionIds = template.Versions.Select(v => v.Id).ToHashSet();
        var orphanIds = existingVersionIds.Where(id => !aggregateVersionIds.Contains(id)).ToList();
        if (orphanIds.Count > 0)
        {
            await connection.ExecuteAsync(@"
                DELETE FROM [dbo].[NotificationTemplateVersions]
                WHERE [Id] IN @Ids",
                new { Ids = orphanIds },
                transaction);
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Release the version FKs before deleting versions, then the template.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationTemplates]
            SET [PublishedVersionId] = NULL, [DraftVersionId] = NULL
            WHERE [Id] = @Id;

            DELETE FROM [dbo].[NotificationTemplateVersions] WHERE [TemplateId] = @Id;

            DELETE FROM [dbo].[NotificationTemplates] WHERE [Id] = @Id;",
            new { Id = id },
            transaction);

        transaction.Commit();
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<NotificationTemplateListItem> Templates, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? notificationTypeId,
        Guid? applicationId,
        NotificationChannelType? channel,
        bool? isPublished,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string fromAndWhere = @"
            FROM [dbo].[NotificationTemplates] t
            INNER JOIN [dbo].[NotificationTypes] nt ON nt.[Id] = t.[NotificationTypeId]
            LEFT JOIN [dbo].[Applications] a ON a.[Id] = t.[ApplicationId]
            LEFT JOIN [dbo].[NotificationTemplateVersions] pv ON pv.[Id] = t.[PublishedVersionId]
            LEFT JOIN [dbo].[NotificationTemplateVersions] dv ON dv.[Id] = t.[DraftVersionId]
            WHERE (@NotificationTypeId IS NULL OR t.[NotificationTypeId] = @NotificationTypeId)
              AND (@ApplicationId IS NULL OR t.[ApplicationId] = @ApplicationId)
              AND (@Channel IS NULL OR t.[Channel] = @Channel)
              AND (@IsPublished IS NULL
                   OR (@IsPublished = 1 AND t.[PublishedVersionId] IS NOT NULL)
                   OR (@IsPublished = 0 AND t.[PublishedVersionId] IS NULL))
              AND (@SearchTerm IS NULL
                   OR nt.[Name] LIKE '%' + @SearchTerm + '%'
                   OR nt.[Code] LIKE '%' + @SearchTerm + '%'
                   OR a.[Name] LIKE '%' + @SearchTerm + '%')";

        var parameters = new
        {
            NotificationTypeId = notificationTypeId,
            ApplicationId = applicationId,
            Channel = (byte?)channel,
            IsPublished = isPublished,
            SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim(),
            Offset = (pageNumber - 1) * pageSize,
            PageSize = pageSize
        };

        var totalCount = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) {fromAndWhere}", parameters);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "nt.[Name] ASC", "t.[Id]");

        var items = await connection.QueryAsync<ListItemDto>($@"
            SELECT t.[Id], t.[NotificationTypeId], nt.[Code] AS TypeCode, nt.[Name] AS TypeName,
                   nt.[IsSystem] AS TypeIsSystem, t.[ApplicationId], a.[Name] AS ApplicationName,
                   t.[Channel], t.[DefaultLanguage],
                   t.[PublishedVersionId], pv.[VersionNumber] AS PublishedVersionNumber,
                   t.[DraftVersionId], dv.[VersionNumber] AS DraftVersionNumber,
                   (SELECT COUNT(*) FROM [dbo].[NotificationTemplateTranslations] tr
                    WHERE tr.[VersionId] = COALESCE(t.[DraftVersionId], t.[PublishedVersionId])) AS TranslationCount,
                   t.[CreatedAt], t.[ModifiedAt]
            {fromAndWhere}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters);

        return (items.Select(i => i.ToReadModel()).ToList(), totalCount);
    }

    #endregion

    #region Send path (published content only)

    /// <inheritdoc />
    public async Task<NotificationTemplateRenderSource?> GetPublishedAsync(
        string typeCode,
        NotificationChannelType channel,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var header = await connection.QueryFirstOrDefaultAsync<PublishedHeaderRow>(@"
            SELECT t.[Id] AS TemplateId, t.[PublishedVersionId], v.[VersionNumber] AS PublishedVersionNumber,
                   t.[ApplicationId], t.[DefaultLanguage]
            FROM [dbo].[NotificationTemplates] t
            INNER JOIN [dbo].[NotificationTypes] nt ON nt.[Id] = t.[NotificationTypeId]
            INNER JOIN [dbo].[NotificationTemplateVersions] v ON v.[Id] = t.[PublishedVersionId]
            WHERE nt.[Code] = @TypeCode
              AND t.[Channel] = @Channel
              AND ((@ApplicationId IS NULL AND t.[ApplicationId] IS NULL)
                   OR t.[ApplicationId] = @ApplicationId)",
            new { TypeCode = typeCode, ApplicationId = applicationId, Channel = (byte)channel });

        if (header is null)
        {
            return null;
        }

        var translations = (await connection.QueryAsync<PublishedTranslationRow>(@"
            SELECT [LanguageCode], [Subject], [BodyHtml], [BodyText]
            FROM [dbo].[NotificationTemplateTranslations]
            WHERE [VersionId] = @VersionId",
            new { VersionId = header.PublishedVersionId }))
            .Select(t => new NotificationTranslationRenderSource(
                t.LanguageCode, t.Subject, t.BodyHtml, t.BodyText))
            .ToList();

        return new NotificationTemplateRenderSource(
            header.TemplateId,
            header.PublishedVersionId,
            header.PublishedVersionNumber,
            header.ApplicationId,
            header.DefaultLanguage,
            translations);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSystemTypeCodesMissingPublishedGlobalTemplateAsync(
        NotificationChannelType channel,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var codes = await connection.QueryAsync<string>(@"
            SELECT nt.[Code]
            FROM [dbo].[NotificationTypes] nt
            WHERE nt.[IsSystem] = 1
              AND nt.[IsActive] = 1
              AND NOT EXISTS (
                  SELECT 1 FROM [dbo].[NotificationTemplates] t
                  WHERE t.[NotificationTypeId] = nt.[Id]
                    AND t.[ApplicationId] IS NULL
                    AND t.[Channel] = @Channel
                    AND t.[PublishedVersionId] IS NOT NULL)
            ORDER BY nt.[Code]",
            new { Channel = (byte)channel });

        return codes.ToList();
    }

    #endregion

    #region Private helpers

    private static async Task InsertVersionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        NotificationTemplateVersion version)
    {
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[NotificationTemplateVersions]
                ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
            VALUES
                (@Id, @TemplateId, @VersionNumber, @ChangeNote, @CreatedAt, @CreatedBy)",
            new { version.Id, version.TemplateId, version.VersionNumber, version.ChangeNote, version.CreatedAt, version.CreatedBy },
            transaction);

        foreach (var translation in version.Translations)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[NotificationTemplateTranslations]
                    ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml], [BodyText], [ModifiedAt], [ModifiedBy])
                VALUES
                    (@Id, @VersionId, @LanguageCode, @Subject, @BodyHtml, @BodyText, @ModifiedAt, @ModifiedBy)",
                new
                {
                    translation.Id,
                    translation.VersionId,
                    translation.LanguageCode,
                    translation.Subject,
                    translation.BodyHtml,
                    translation.BodyText,
                    translation.ModifiedAt,
                    translation.ModifiedBy
                },
                transaction);
        }
    }

    private static async Task SyncDraftTranslationsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        NotificationTemplateVersion draft)
    {
        var existingLanguages = (await connection.QueryAsync<string>(@"
            SELECT [LanguageCode] FROM [dbo].[NotificationTemplateTranslations]
            WHERE [VersionId] = @VersionId",
            new { VersionId = draft.Id },
            transaction)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var translation in draft.Translations)
        {
            if (existingLanguages.Contains(translation.LanguageCode))
            {
                await connection.ExecuteAsync(@"
                    UPDATE [dbo].[NotificationTemplateTranslations]
                    SET [Subject] = @Subject,
                        [BodyHtml] = @BodyHtml,
                        [BodyText] = @BodyText,
                        [ModifiedAt] = @ModifiedAt,
                        [ModifiedBy] = @ModifiedBy
                    WHERE [VersionId] = @VersionId AND [LanguageCode] = @LanguageCode",
                    new
                    {
                        VersionId = draft.Id,
                        translation.LanguageCode,
                        translation.Subject,
                        translation.BodyHtml,
                        translation.BodyText,
                        translation.ModifiedAt,
                        translation.ModifiedBy
                    },
                    transaction);
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO [dbo].[NotificationTemplateTranslations]
                        ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml], [BodyText], [ModifiedAt], [ModifiedBy])
                    VALUES
                        (@Id, @VersionId, @LanguageCode, @Subject, @BodyHtml, @BodyText, @ModifiedAt, @ModifiedBy)",
                    new
                    {
                        translation.Id,
                        VersionId = draft.Id,
                        translation.LanguageCode,
                        translation.Subject,
                        translation.BodyHtml,
                        translation.BodyText,
                        translation.ModifiedAt,
                        translation.ModifiedBy
                    },
                    transaction);
            }
        }

        var aggregateLanguages = draft.Translations
            .Select(t => t.LanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedLanguages = existingLanguages.Where(l => !aggregateLanguages.Contains(l)).ToList();
        if (removedLanguages.Count > 0)
        {
            await connection.ExecuteAsync(@"
                DELETE FROM [dbo].[NotificationTemplateTranslations]
                WHERE [VersionId] = @VersionId AND [LanguageCode] IN @Languages",
                new { VersionId = draft.Id, Languages = removedLanguages },
                transaction);
        }
    }

    private static async Task UpdatePointersAndAuditAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        NotificationTemplate template)
    {
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[NotificationTemplates]
            SET [DefaultLanguage] = @DefaultLanguage,
                [PublishedVersionId] = @PublishedVersionId,
                [DraftVersionId] = @DraftVersionId,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                template.Id,
                template.DefaultLanguage,
                template.PublishedVersionId,
                template.DraftVersionId,
                template.ModifiedAt,
                template.ModifiedBy
            },
            transaction);
    }

    #endregion

    #region Row DTOs

    private record TemplateDto
    {
        public Guid Id { get; init; }
        public Guid NotificationTypeId { get; init; }
        public Guid? ApplicationId { get; init; }
        public byte Channel { get; init; }
        public string DefaultLanguage { get; init; } = "en";
        public Guid? PublishedVersionId { get; init; }
        public Guid? DraftVersionId { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public NotificationTemplate ToEntity(IEnumerable<NotificationTemplateVersion> versions) => new(
            Id,
            NotificationTypeId,
            ApplicationId,
            (NotificationChannelType)Channel,
            DefaultLanguage,
            PublishedVersionId,
            DraftVersionId,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy,
            versions);
    }

    private record VersionDto
    {
        public Guid Id { get; init; }
        public Guid TemplateId { get; init; }
        public int VersionNumber { get; init; }
        public string? ChangeNote { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }

        public NotificationTemplateVersion ToEntity(
            IEnumerable<NotificationTemplateTranslation> translations) => new(
            Id,
            TemplateId,
            VersionNumber,
            ChangeNote,
            CreatedAt,
            CreatedBy,
            translations);
    }

    private record TranslationDto
    {
        public Guid Id { get; init; }
        public Guid VersionId { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string BodyHtml { get; init; } = string.Empty;
        public string? BodyText { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public NotificationTemplateTranslation ToEntity() => new(
            Id,
            VersionId,
            LanguageCode,
            Subject,
            BodyHtml,
            BodyText,
            ModifiedAt,
            ModifiedBy);
    }

    private record ListItemDto
    {
        public Guid Id { get; init; }
        public Guid NotificationTypeId { get; init; }
        public string TypeCode { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public bool TypeIsSystem { get; init; }
        public Guid? ApplicationId { get; init; }
        public string? ApplicationName { get; init; }
        public byte Channel { get; init; }
        public string DefaultLanguage { get; init; } = "en";
        public Guid? PublishedVersionId { get; init; }
        public int? PublishedVersionNumber { get; init; }
        public Guid? DraftVersionId { get; init; }
        public int? DraftVersionNumber { get; init; }
        public int TranslationCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ModifiedAt { get; init; }

        public NotificationTemplateListItem ToReadModel() => new(
            Id,
            NotificationTypeId,
            TypeCode,
            TypeName,
            TypeIsSystem,
            ApplicationId,
            ApplicationName,
            Channel,
            DefaultLanguage,
            PublishedVersionId,
            PublishedVersionNumber,
            DraftVersionId,
            DraftVersionNumber,
            TranslationCount,
            CreatedAt,
            ModifiedAt);
    }

    private record PublishedHeaderRow
    {
        public Guid TemplateId { get; init; }
        public Guid PublishedVersionId { get; init; }
        public int PublishedVersionNumber { get; init; }
        public Guid? ApplicationId { get; init; }
        public string DefaultLanguage { get; init; } = "en";
    }

    private record PublishedTranslationRow
    {
        public string LanguageCode { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string BodyHtml { get; init; } = string.Empty;
        public string? BodyText { get; init; }
    }

    #endregion
}
