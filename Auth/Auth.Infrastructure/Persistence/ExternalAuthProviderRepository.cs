using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the external auth provider repository.
/// </summary>
public class ExternalAuthProviderRepository : IExternalAuthProviderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ExternalAuthProviderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalAuthProvider>> GetAllEnabledAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var providers = await connection.QueryAsync<ExternalAuthProvider>(@"
            SELECT [Id], [Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder], [CreatedAt], [ModifiedAt]
            FROM [dbo].[ExternalAuthProviders]
            WHERE [IsEnabled] = 1
            ORDER BY [DisplayOrder]");

        return providers.ToList();
    }

    /// <inheritdoc />
    public async Task<ExternalAuthProvider?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<ExternalAuthProvider>(@"
            SELECT [Id], [Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder], [CreatedAt], [ModifiedAt]
            FROM [dbo].[ExternalAuthProviders]
            WHERE [Code] = @Code",
            new { Code = code });
    }
}
