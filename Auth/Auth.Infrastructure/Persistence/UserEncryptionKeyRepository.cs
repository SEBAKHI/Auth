using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the per-user encryption key repository.
/// </summary>
public class UserEncryptionKeyRepository : IUserEncryptionKeyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserEncryptionKeyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserEncryptionKey?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserEncryptionKey>(@"
            SELECT [UserId], [WrappedDek], [KeyVersion], [Algorithm], [CreatedAt]
            FROM [dbo].[UserEncryptionKeys]
            WHERE [UserId] = @UserId",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task CreateAsync(UserEncryptionKey key, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserEncryptionKeys] ([UserId], [WrappedDek], [KeyVersion], [Algorithm], [CreatedAt])
            VALUES (@UserId, @WrappedDek, @KeyVersion, @Algorithm, @CreatedAt)",
            new { key.UserId, key.WrappedDek, key.KeyVersion, key.Algorithm, key.CreatedAt });
    }

    /// <inheritdoc />
    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[UserEncryptionKeys] WHERE [UserId] = @UserId",
            new { UserId = userId });
    }
}
