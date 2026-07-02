using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
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

    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.ExternalProviders.Name, ["[Name]"]),
        (SortFields.ExternalProviders.Code, ["[Code]"]),
        (SortFields.ExternalProviders.DisplayOrder, ["[DisplayOrder]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalAuthProvider>> GetAllEnabledAsync(
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[DisplayOrder]", "[Id]");
        var providers = await connection.QueryAsync<ExternalAuthProvider>($@"
            SELECT [Id], [Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder], [CreatedAt], [ModifiedAt]
            FROM [dbo].[ExternalAuthProviders]
            WHERE [IsEnabled] = 1
            ORDER BY {orderBy}");

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
