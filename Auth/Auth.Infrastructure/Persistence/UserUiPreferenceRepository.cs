using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the per-user UI preference repository.
/// </summary>
public class UserUiPreferenceRepository : IUserUiPreferenceRepository
{
    private const string SelectColumns = "[Id], [UserId], [Key], [Value], [ModifiedAt]";

    private readonly IDbConnectionFactory _connectionFactory;

    public UserUiPreferenceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserUiPreference>> GetAllForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<UserUiPreferenceDto>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserUiPreferences]
            WHERE [UserId] = @UserId",
            new { UserId = userId });

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[UserUiPreferences] WHERE [UserId] = @UserId",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task UpsertAsync(UserUiPreference preference, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // UPDATE-then-INSERT rather than MERGE: two tabs writing the same key
        // race, and the loser must land as an update instead of violating
        // UQ_UserUiPreferences_UserKey. Last write wins by design.
        var updated = await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserUiPreferences]
            SET [Value] = @Value, [ModifiedAt] = @ModifiedAt
            WHERE [UserId] = @UserId AND [Key] = @Key",
            new { preference.UserId, preference.Key, preference.Value, preference.ModifiedAt });

        if (updated > 0)
        {
            return;
        }

        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[UserUiPreferences] ([Id], [UserId], [Key], [Value], [ModifiedAt])
                VALUES (@Id, @UserId, @Key, @Value, @ModifiedAt)",
                new
                {
                    preference.Id,
                    preference.UserId,
                    preference.Key,
                    preference.Value,
                    preference.ModifiedAt
                });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent insert won the race; apply this write over it.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[UserUiPreferences]
                SET [Value] = @Value, [ModifiedAt] = @ModifiedAt
                WHERE [UserId] = @UserId AND [Key] = @Key",
                new { preference.UserId, preference.Key, preference.Value, preference.ModifiedAt });
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[UserUiPreferences]
            WHERE [UserId] = @UserId AND [Key] = @Key",
            new { UserId = userId, Key = key });
    }

    private record UserUiPreferenceDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string Key { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public DateTime ModifiedAt { get; init; }

        public UserUiPreference ToEntity() => new(Id, UserId, Key, Value, ModifiedAt);
    }
}
