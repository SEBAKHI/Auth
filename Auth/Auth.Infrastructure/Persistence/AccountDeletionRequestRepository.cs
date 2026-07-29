using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the account deletion request repository. Terminal
/// rows are destruction evidence: there is deliberately no delete method.
/// </summary>
public class AccountDeletionRequestRepository : IAccountDeletionRequestRepository
{
    private const string Columns =
        "[Id], [UserId], [Status], [Source], [RequestedAtUtc], [GraceEndsAtUtc], " +
        "[CancelledAtUtc], [CompletedAtUtc], [PolicyVersion], [AttemptCount], [LastError], " +
        "[CreatedAt], [CreatedBy]";

    private readonly IDbConnectionFactory _connectionFactory;

    public AccountDeletionRequestRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(AccountDeletionRequest request, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[AccountDeletionRequests] (
                    [Id], [UserId], [Status], [Source], [RequestedAtUtc], [GraceEndsAtUtc],
                    [CancelledAtUtc], [CompletedAtUtc], [PolicyVersion], [AttemptCount], [LastError],
                    [CreatedAt], [CreatedBy]
                ) VALUES (
                    @Id, @UserId, @Status, @Source, @RequestedAtUtc, @GraceEndsAtUtc,
                    @CancelledAtUtc, @CompletedAtUtc, @PolicyVersion, @AttemptCount, @LastError,
                    @CreatedAt, @CreatedBy
                )",
                ToParameters(request));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Lost the race on the filtered unique active-request index: an
            // active request already exists for this user.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AccountDeletionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<AccountDeletionRequest>($@"
            SELECT {Columns} FROM [dbo].[AccountDeletionRequests]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task<AccountDeletionRequest?> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<AccountDeletionRequest>($@"
            SELECT {Columns} FROM [dbo].[AccountDeletionRequests]
            WHERE [UserId] = @UserId
              AND [Status] IN (@PendingGrace, @Processing)",
            new
            {
                UserId = userId,
                PendingGrace = (int)AccountDeletionStatus.PendingGrace,
                Processing = (int)AccountDeletionStatus.Processing
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDeletionRequest>> GetDueAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var results = await connection.QueryAsync<AccountDeletionRequest>($@"
            SELECT TOP (@BatchSize) {Columns} FROM [dbo].[AccountDeletionRequests]
            WHERE [Status] = @PendingGrace AND [GraceEndsAtUtc] <= @UtcNow
            ORDER BY [GraceEndsAtUtc]",
            new
            {
                BatchSize = batchSize,
                PendingGrace = (int)AccountDeletionStatus.PendingGrace,
                UtcNow = utcNow
            });

        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        AccountDeletionRequest request, AccountDeletionStatus expectedStatus, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The expected-status predicate is the optimistic guard that gives the
        // recovery-vs-claim race exactly one winner.
        var affected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[AccountDeletionRequests] SET
                [Status] = @Status,
                [CancelledAtUtc] = @CancelledAtUtc,
                [CompletedAtUtc] = @CompletedAtUtc,
                [AttemptCount] = @AttemptCount,
                [LastError] = @LastError
            WHERE [Id] = @Id AND [Status] = @ExpectedStatus",
            new
            {
                request.Id,
                Status = (int)request.Status,
                request.CancelledAtUtc,
                request.CompletedAtUtc,
                request.AttemptCount,
                request.LastError,
                ExpectedStatus = (int)expectedStatus
            });

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<int> ReclaimProcessingAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(@"
            UPDATE [dbo].[AccountDeletionRequests]
            SET [Status] = @PendingGrace
            WHERE [Status] = @Processing",
            new
            {
                PendingGrace = (int)AccountDeletionStatus.PendingGrace,
                Processing = (int)AccountDeletionStatus.Processing
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDeletionRequest>> GetCompletedWithLiveUserAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var results = await connection.QueryAsync<AccountDeletionRequest>($@"
            SELECT TOP (@BatchSize) {SelectColumns("r")}
            FROM [dbo].[AccountDeletionRequests] r
            INNER JOIN [dbo].[Users] u ON u.[Id] = r.[UserId]
            WHERE r.[Status] = @Completed
            ORDER BY r.[CompletedAtUtc]",
            new { BatchSize = batchSize, Completed = (int)AccountDeletionStatus.Completed });

        return results.ToList();
    }

    private static string SelectColumns(string alias) =>
        string.Join(", ", Columns.Split(", ").Select(c => $"{alias}.{c}"));

    private static object ToParameters(AccountDeletionRequest request) => new
    {
        request.Id,
        request.UserId,
        Status = (int)request.Status,
        Source = (int)request.Source,
        request.RequestedAtUtc,
        request.GraceEndsAtUtc,
        request.CancelledAtUtc,
        request.CompletedAtUtc,
        request.PolicyVersion,
        request.AttemptCount,
        request.LastError,
        request.CreatedAt,
        request.CreatedBy
    };
}
