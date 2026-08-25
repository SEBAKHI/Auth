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
    public async Task<IReadOnlyList<string>> ReclaimUnattachedAsync(
        DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // OUTPUT so the delete and the listing are one statement: reading the
        // keys first and deleting them second would reclaim a row that was
        // attached in between, and the caller would then delete a file something
        // had just started pointing at.
        var reclaimed = await connection.QueryAsync<string>(@"
            DELETE FROM [dbo].[UploadedImages]
            OUTPUT DELETED.[StorageKey]
            WHERE [AttachedAt] IS NULL
              AND [UploadedAt] < @OlderThan",
            new { OlderThan = olderThan });

        return reclaimed.ToList();
    }
}
