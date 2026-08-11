using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the secret-operation challenge repository.
/// </summary>
/// <remarks>
/// The two state transitions that matter — verifying and spending — are single
/// conditional UPDATEs whose row count is the answer. Reading, deciding and
/// writing separately would let two concurrent requests both pass the read and
/// both rotate a key.
/// </remarks>
public class SecretOperationChallengeRepository : ISecretOperationChallengeRepository
{
    private const string SelectColumns = @"
        [Id], [RequestedBy], [Operation], [PayloadHash], [CodeHash], [ExpiresAt],
        [VerifiedAt], [ApprovalExpiresAt], [UsedAt], [AttemptCount], [IpAddress], [CreatedAt]";

    private readonly IDbConnectionFactory _connectionFactory;

    public SecretOperationChallengeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<SecretOperationChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<SecretOperationChallengeDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[SecretOperationChallenges]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(SecretOperationChallenge challenge, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[SecretOperationChallenges] (
                [Id], [RequestedBy], [Operation], [PayloadHash], [CodeHash], [ExpiresAt],
                [VerifiedAt], [ApprovalExpiresAt], [UsedAt], [AttemptCount], [IpAddress], [CreatedAt]
            ) VALUES (
                @Id, @RequestedBy, @Operation, @PayloadHash, @CodeHash, @ExpiresAt,
                @VerifiedAt, @ApprovalExpiresAt, @UsedAt, @AttemptCount, @IpAddress, @CreatedAt
            )",
            new
            {
                challenge.Id,
                challenge.RequestedBy,
                Operation = (byte)challenge.Operation,
                challenge.PayloadHash,
                challenge.CodeHash,
                challenge.ExpiresAt,
                challenge.VerifiedAt,
                challenge.ApprovalExpiresAt,
                challenge.UsedAt,
                challenge.AttemptCount,
                challenge.IpAddress,
                challenge.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task<bool> MarkVerifiedAsync(
        Guid id,
        DateTime verifiedAt,
        DateTime approvalExpiresAt,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The WHERE clause repeats every open-challenge condition so a row that
        // expired or was verified between the read and this write loses. The
        // attempt term is a second line of defence: TryRegisterAttemptAsync
        // already refuses to hand out a slot past the cap, so a correct code can
        // only arrive here having claimed one — this clause keeps that true if
        // the reserve-then-evaluate order in the service is ever undone.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[SecretOperationChallenges] SET
                [VerifiedAt] = @VerifiedAt,
                [ApprovalExpiresAt] = @ApprovalExpiresAt
            WHERE [Id] = @Id
              AND [UsedAt] IS NULL
              AND [VerifiedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
              AND [AttemptCount] <= @MaxAttempts",
            new { Id = id, VerifiedAt = verifiedAt, ApprovalExpiresAt = approvalExpiresAt, MaxAttempts = maxAttempts });

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Single use lives here: the first UPDATE stamps UsedAt, every later one
        // matches nothing.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[SecretOperationChallenges] SET
                [UsedAt] = GETUTCDATE()
            WHERE [Id] = @Id
              AND [UsedAt] IS NULL
              AND [VerifiedAt] IS NOT NULL
              AND [ApprovalExpiresAt] > GETUTCDATE()",
            new { Id = id });

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryRegisterAttemptAsync(
        Guid id,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The cap lives in this WHERE clause, exactly as single use lives in
        // TryConsumeAsync's. An unconditional increment would only *record*
        // attempts; it would not refuse the sixth, because every request that
        // read the row before the first increment committed sees the same count.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[SecretOperationChallenges] SET
                [AttemptCount] = [AttemptCount] + 1
            WHERE [Id] = @Id
              AND [UsedAt] IS NULL
              AND [VerifiedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
              AND [AttemptCount] < @MaxAttempts",
            new { Id = id, MaxAttempts = maxAttempts });

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Both windows must have closed. Deleting on ExpiresAt alone would drop
        // a row verified just before its entry deadline while its five-minute
        // approval window is still open, failing the administrator's operation
        // at the moment they confirm it. Unspent rows are found through
        // IX_SecretOperationChallenges_ExpiresAt (filtered on UsedAt IS NULL);
        // spent rows always carry an ApprovalExpiresAt, so they age out too.
        return await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[SecretOperationChallenges]
            WHERE [ExpiresAt] <= GETUTCDATE()
              AND ([ApprovalExpiresAt] IS NULL OR [ApprovalExpiresAt] <= GETUTCDATE())");
    }

    /// <inheritdoc />
    public async Task InvalidateOutstandingForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Closed by stamping UsedAt: an unspent challenge and a spent one are
        // both unusable, and one column carries "no longer live" for the whole
        // table, including the sweep's filtered index.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[SecretOperationChallenges] SET
                [UsedAt] = GETUTCDATE()
            WHERE [RequestedBy] = @UserId
              AND [UsedAt] IS NULL",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<int> GetRecentCountForUserAsync(
        Guid userId,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[SecretOperationChallenges]
            WHERE [RequestedBy] = @UserId
              AND [CreatedAt] > DATEADD(SECOND, -@WindowSeconds, GETUTCDATE())",
            new { UserId = userId, WindowSeconds = (int)window.TotalSeconds });
    }

    // Internal DTO for mapping from database
    private record SecretOperationChallengeDto
    {
        public Guid Id { get; init; }
        public Guid RequestedBy { get; init; }
        public byte Operation { get; init; }
        public string? PayloadHash { get; init; }
        public string CodeHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? VerifiedAt { get; init; }
        public DateTime? ApprovalExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public string? IpAddress { get; init; }
        public DateTime CreatedAt { get; init; }

        public SecretOperationChallenge ToEntity() => new(
            Id,
            RequestedBy,
            (SecretOperation)Operation,
            PayloadHash,
            CodeHash,
            ExpiresAt,
            VerifiedAt,
            ApprovalExpiresAt,
            UsedAt,
            AttemptCount,
            IpAddress,
            CreatedAt);
    }
}
