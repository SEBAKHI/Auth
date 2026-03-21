using System.Data;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Factory interface for creating database connections.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new database connection.
    /// </summary>
    /// <returns>An open database connection.</returns>
    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken);
}
