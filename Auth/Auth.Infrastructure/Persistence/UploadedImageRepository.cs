using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <inheritdoc cref="IUploadedImageRepository" />
public class UploadedImageRepository : IUploadedImageRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UploadedImageRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        string storageKey, Guid uploadedBy, long sizeBytes, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UploadedImages] ([StorageKey], [UploadedBy], [SizeBytes])
            VALUES (@StorageKey, @UploadedBy, @SizeBytes)",
            new { StorageKey = storageKey, UploadedBy = uploadedBy, SizeBytes = sizeBytes });
    }

    /// <inheritdoc />
    public async Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // COALESCE because SUM over no rows is NULL, and a user with no uploads
        // occupies zero rather than an unknown amount.
        return await connection.ExecuteScalarAsync<long>(@"
            SELECT COALESCE(SUM([SizeBytes]), 0)
            FROM [dbo].[UploadedImages]
            WHERE [UploadedBy] = @UserId",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<bool> TryAttachAsync(
        string storageKey, Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Both predicates in the UPDATE rather than a read followed by a write:
        // two callers racing for the same key would both pass a separate check,
        // and the second attach would silently steal a file the first is already
        // displaying. Here exactly one UPDATE affects a row.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[UploadedImages]
            SET [AttachedAt] = GETUTCDATE()
            WHERE [StorageKey] = @StorageKey
              AND [UploadedBy] = @UserId
              AND [AttachedAt] IS NULL",
            new { StorageKey = storageKey, UserId = userId });

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(
        string storageKey, Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement for the same reason as TryAttachAsync: the ownership
        // predicate and the write cannot be split by another caller. COALESCE
        // keeps the first attach time, so a key already in use by its uploader
        // still counts as one affected row.
        var affected = await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[UploadedImages]
            SET [AttachedAt] = COALESCE([AttachedAt], GETUTCDATE())
            WHERE [StorageKey] = @StorageKey
              AND [UploadedBy] = @UserId",
            new { StorageKey = storageKey, UserId = userId },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    // TRUE when a column that can hold an upload names u.[StorageKey].
    //
    // Soft-deleted rows count: a restore must find its image. A single-value
    // column holds the key, or, on rows written before key normalization, the
    // composed absolute URL, which ends with "/" + key. An HTML column holds the
    // composed URL anywhere in its text. Every template version counts, published
    // or not, so a rollback finds its images.
    //
    // Keys are minted only by the storage service's SaveImageAsync, as
    // "{guid:N}.webp": 32 hex digits, a dot and "webp". So the LIKE pattern built
    // from a key has no wildcard to escape.
    private const string Referenced = @"
                EXISTS (SELECT 1 FROM [dbo].[Users] r
                        WHERE r.[ProfileImageUrl] = u.[StorageKey]
                           OR r.[ProfileImageUrl] LIKE N'%/' + u.[StorageKey])
             OR EXISTS (SELECT 1 FROM [dbo].[Applications] r
                        WHERE r.[LogoUrl] = u.[StorageKey]
                           OR r.[LogoUrl] LIKE N'%/' + u.[StorageKey])
             OR EXISTS (SELECT 1 FROM [dbo].[Organizations] r
                        WHERE r.[LogoUrl] = u.[StorageKey]
                           OR r.[LogoUrl] LIKE N'%/' + u.[StorageKey])
             OR EXISTS (SELECT 1 FROM [dbo].[PlatformSettings] r
                        WHERE r.[LogoUrl] = u.[StorageKey]
                           OR r.[LogoUrl] LIKE N'%/' + u.[StorageKey]
                           OR r.[LogoUrlDark] = u.[StorageKey]
                           OR r.[LogoUrlDark] LIKE N'%/' + u.[StorageKey]
                           OR r.[FaviconUrl] = u.[StorageKey]
                           OR r.[FaviconUrl] LIKE N'%/' + u.[StorageKey])
             OR EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateTranslations] r
                        WHERE CHARINDEX(u.[StorageKey], r.[BodyHtml]) > 0
                           OR CHARINDEX(u.[StorageKey], r.[BodyText]) > 0)
             OR EXISTS (SELECT 1 FROM [dbo].[NotificationLayouts] r
                        WHERE CHARINDEX(u.[StorageKey], r.[DraftContent]) > 0
                           OR CHARINDEX(u.[StorageKey], r.[PublishedContent]) > 0)";

    // The sweep walks the unattached rows in key order, a batch at a time (the
    // caller drives the walk). The reference check costs (rows examined) x (rows
    // in the referencing tables), and an upload costs its uploader almost
    // nothing: a tiny image is a few bytes against a quota counted in bytes. One
    // account can therefore queue thousands of unattached rows, and a statement
    // over all of them would pass the command timeout on every run: nothing
    // lost, since the transaction rolls back, but nothing reclaimed again
    // either. A batch keeps each statement short. Measured with ten times the
    // seeded template text: 50 keys took up to 17 s per batch, close to the
    // default 30-second command timeout; 20 keeps a wide margin.
    private const int SweepBatchSize = 20;

    // The last key of the next batch, or NULL when no unattached row is left.
    private const string NextBatchEndSql = @"
            SELECT MAX(b.[StorageKey])
            FROM (SELECT TOP (@BatchSize) [StorageKey]
                  FROM [dbo].[UploadedImages]
                  WHERE [AttachedAt] IS NULL
                    AND [StorageKey] > @After
                  ORDER BY [StorageKey]) b";

    // No age filter: an upload referenced today is kept from today, however
    // young it is.
    private const string AdoptReferencedSql = $@"
            UPDATE u
            SET [AttachedAt] = GETUTCDATE()
            FROM [dbo].[UploadedImages] u
            WHERE u.[AttachedAt] IS NULL
              AND u.[StorageKey] > @After
              AND u.[StorageKey] <= @Last
              AND ({Referenced})";

    private const string ReclaimUnreferencedSql = $@"
            DELETE u
            OUTPUT DELETED.[StorageKey]
            FROM [dbo].[UploadedImages] u
            WHERE u.[AttachedAt] IS NULL
              AND u.[StorageKey] > @After
              AND u.[StorageKey] <= @Last
              AND u.[UploadedAt] < @OlderThan
              AND NOT ({Referenced})";

    /// <inheritdoc />
    public async Task<UploadSweepBatch?> SweepBatchAsync(
        string after, DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var last = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            NextBatchEndSql,
            new { BatchSize = SweepBatchSize, After = after },
            cancellationToken: cancellationToken));
        if (last is null)
        {
            return null;
        }

        // One transaction for the batch: the adopt and the reclaim see the same
        // rows, and a failure undoes both.
        using var transaction = connection.BeginTransaction();
        var range = new { After = after, Last = last, OlderThan = olderThan };

        var adopted = await connection.ExecuteAsync(new CommandDefinition(
            AdoptReferencedSql, range, transaction, cancellationToken: cancellationToken));

        // OUTPUT so the delete and the listing are one statement: reading the
        // keys first and deleting them second would reclaim a row that was
        // attached in between, and the caller would then delete a file something
        // had just started pointing at. The reference check is repeated here for
        // the same reason: a template saved between the adopt and this statement
        // references a key the adopt did not see.
        var reclaimed = (await connection.QueryAsync<string>(new CommandDefinition(
            ReclaimUnreferencedSql, range, transaction, cancellationToken: cancellationToken))).ToList();

        transaction.Commit();

        return new UploadSweepBatch(last, adopted, reclaimed);
    }
}
