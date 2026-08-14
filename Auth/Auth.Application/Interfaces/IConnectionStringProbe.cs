namespace Auth.Application.Interfaces;

/// <summary>
/// Outcome of examining a candidate database connection string.
/// </summary>
/// <param name="IsWellFormed">
/// False when the text cannot be parsed as a connection string at all. This can
/// never become true later, so it is a hard rejection.
/// </param>
/// <param name="CanConnect">
/// Whether a connection was actually opened. False is not necessarily an error:
/// an administrator staging a password that is not yet active at the server will
/// legitimately store a value that cannot connect until they switch it over.
/// </param>
/// <param name="Detail">
/// Short diagnostic for the operator. Implementations must never place the
/// candidate connection string, or any credential drawn from it, in here.
/// </param>
public sealed record ConnectionProbeResult(
    bool IsWellFormed,
    bool CanConnect,
    string? Detail);

/// <summary>
/// Tests a candidate database connection string before it is committed to the
/// encrypted secrets file.
/// </summary>
/// <remarks>
/// Exists so the Application layer can validate a connection string without
/// referencing a database driver: the only implementation lives in the
/// Infrastructure layer, where SQL Server types belong.
/// <para>
/// This is a guard against typos, not an authorization check. The caller decides
/// what an unreachable server means — see
/// <c>SetConnectionStringCommandHandler</c>.
/// </para>
/// </remarks>
public interface IConnectionStringProbe
{
    /// <summary>
    /// Parses the connection string and attempts to open a connection with a
    /// short timeout. Never throws for an unusable value — failures are reported
    /// through <see cref="ConnectionProbeResult"/>.
    /// </summary>
    Task<ConnectionProbeResult> ProbeAsync(string connectionString, CancellationToken cancellationToken);
}
