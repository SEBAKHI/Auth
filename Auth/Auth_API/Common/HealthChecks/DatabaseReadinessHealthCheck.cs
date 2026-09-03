using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that proves the database is reachable — once per window,
/// no matter how many callers ask.
/// </summary>
/// <remarks>
/// <para>
/// <c>/ready</c> is reachable without the gateway token, so it is the one
/// place an anonymous, unattributable caller can make this process open a
/// database connection. The stock SQL Server check opened one per request,
/// which meant a burst of free GETs could hold every pooled connection for the
/// probe's five-second timeout and starve sign-in behind it. This check keeps
/// the same probe and the same timeout but adds two things: the result is
/// <b>cached</b> for <see cref="DefaultCacheTtl"/>, and while it is stale only
/// <b>one</b> caller runs the probe — everyone else waits for that result
/// (single-flight). A thousand requests a second therefore cost one connection
/// every five seconds. A failing result is cached the same way, so an outage
/// is not hammered by its own monitors, and it still surfaces within one TTL,
/// which is faster than any monitor polls.
/// </para>
/// <para>
/// The second constructor is the test seam: any probe, any clock.
/// </para>
/// </remarks>
public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    /// <summary>How long a probe result is served before the database is asked again.</summary>
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Bounded explicitly: the database is on a remote host, and a probe that
    /// hangs on a network stall is worse than one that reports Degraded.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<CancellationToken, Task<HealthCheckResult>> _probe;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // One immutable pair swapped atomically, so a reader never sees a fresh
    // timestamp beside a stale result.
    private volatile CachedResult? _cached;

    private sealed record CachedResult(HealthCheckResult Result, DateTimeOffset At);

    public DatabaseReadinessHealthCheck(string connectionString)
        : this(cancellationToken => ProbeAsync(connectionString, cancellationToken), DefaultCacheTtl, () => DateTimeOffset.UtcNow)
    {
    }

    public DatabaseReadinessHealthCheck(
        Func<CancellationToken, Task<HealthCheckResult>> probe,
        TimeSpan ttl,
        Func<DateTimeOffset> clock)
    {
        _probe = probe;
        _ttl = ttl;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (TryGetFresh(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Someone else may have probed while this caller waited at the gate.
            if (TryGetFresh(out cached))
            {
                return cached;
            }

            var result = await _probe(cancellationToken);
            _cached = new CachedResult(result, _clock());
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out HealthCheckResult result)
    {
        var snapshot = _cached;
        if (snapshot is not null && _clock() - snapshot.At < _ttl)
        {
            result = snapshot.Result;
            return true;
        }

        result = default;
        return false;
    }

    private static async Task<HealthCheckResult> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy("Database is reachable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"Database probe timed out after {ProbeTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Database is not reachable.", ex);
        }
    }
}
