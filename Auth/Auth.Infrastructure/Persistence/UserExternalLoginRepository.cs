using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the user external login repository.
/// </summary>
public class UserExternalLoginRepository : IUserExternalLoginRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserExternalLoginRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserExternalLogin?> GetByProviderAsync(
        string provider,
        string providerUserId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserExternalLogin>(@"
            SELECT [Id], [UserId], [Provider], [ProviderUserId], [Email], [Name], [PictureUrl], [ProviderRefreshTokenEnc], [CreatedAt], [ModifiedAt]
            FROM [dbo].[UserExternalLogins]
            WHERE [Provider] = @Provider AND [ProviderUserId] = @ProviderUserId",
            new { Provider = provider, ProviderUserId = providerUserId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserExternalLogin>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var logins = await connection.QueryAsync<UserExternalLogin>(@"
            SELECT [Id], [UserId], [Provider], [ProviderUserId], [Email], [Name], [PictureUrl], [ProviderRefreshTokenEnc], [CreatedAt], [ModifiedAt]
            FROM [dbo].[UserExternalLogins]
            WHERE [UserId] = @UserId",
            new { UserId = userId });

        return logins.ToList();
    }

    /// <inheritdoc />
    public async Task CreateAsync(UserExternalLogin login, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserExternalLogins]
                ([Id], [UserId], [Provider], [ProviderUserId], [Email], [Name], [PictureUrl], [CreatedAt])
            VALUES
                (@Id, @UserId, @Provider, @ProviderUserId, @Email, @Name, @PictureUrl, @CreatedAt)",
            new
            {
                login.Id,
                login.UserId,
                login.Provider,
                login.ProviderUserId,
                login.Email,
                login.Name,
                login.PictureUrl,
                login.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task UpdateAsync(UserExternalLogin login, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserExternalLogins]
            SET [Email] = @Email,
                [Name] = @Name,
                [PictureUrl] = @PictureUrl,
                [ModifiedAt] = @ModifiedAt
            WHERE [Id] = @Id",
            new
            {
                login.Id,
                login.Email,
                login.Name,
                login.PictureUrl,
                login.ModifiedAt
            });
    }

    /// <inheritdoc />
    public async Task UpdateProviderRefreshTokenAsync(
        Guid loginId, string? encryptedToken, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserExternalLogins]
            SET [ProviderRefreshTokenEnc] = @EncryptedToken, [ModifiedAt] = GETUTCDATE()
            WHERE [Id] = @Id",
            new { Id = loginId, EncryptedToken = encryptedToken });
    }
}
