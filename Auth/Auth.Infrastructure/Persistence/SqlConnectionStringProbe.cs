using Auth.Application.Interfaces;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of <see cref="IConnectionStringProbe"/>.
/// </summary>
public class SqlConnectionStringProbe : IConnectionStringProbe
{
    /// <summary>
    /// Bounds how long an administrator waits on the dialog, and stops an
    /// unreachable host from holding a request open for the driver's default of
    /// 15 seconds.
    /// </summary>
    private const int ProbeTimeoutSeconds = 5;

    /// <inheritdoc />
    public async Task<ConnectionProbeResult> ProbeAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        SqlConnectionStringBuilder builder;

        try
        {
            builder = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = ProbeTimeoutSeconds
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Names the offending keyword, never a value.
            return new ConnectionProbeResult(IsWellFormed: false, CanConnect: false, Detail: ex.Message);
        }

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            return new ConnectionProbeResult(IsWellFormed: true, CanConnect: true, Detail: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // SqlException reports the server, the database or the login name —
            // never the password — so the message is safe to show the operator
            // and is the only thing that makes a failure actionable.
            return new ConnectionProbeResult(IsWellFormed: true, CanConnect: false, Detail: ex.Message);
        }
    }
}
