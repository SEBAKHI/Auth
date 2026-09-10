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

    /// <summary>
    /// A piece of schema the running code depends on, and the query that
    /// answers 1 when the database has it.
    /// </summary>
    public sealed record SchemaExpectation(string Name, string Sql);

    /// <summary>
    /// Schema this build cannot run without. The database is published by hand
    /// from Visual Studio, separately from the API upload, and the two have been
    /// deployed out of order before — the API then fails deep inside a request,
    /// as a 500 with a SQL error in the log, and nothing names the cause.
    /// Reported here as Degraded, which /ready surfaces to the operator before
    /// any user meets the failure. Each entry says which half is missing.
    /// </summary>
    public static readonly IReadOnlyList<SchemaExpectation> SchemaExpectations =
    [
        // Username became the full address, so the column must hold one.
        // COL_LENGTH reports bytes; NVARCHAR(255) is 510.
        new("Users.Username widened to NVARCHAR(255)",
            "SELECT CASE WHEN COL_LENGTH('dbo.Users', 'Username') >= 510 THEN 1 ELSE 0 END"),
        // The pending-registration table: its repository is swept daily and
        // will be written by the registration flow, both of which fail as
        // "Invalid object name" without it.
        new("PendingRegistrations table",
            "SELECT CASE WHEN OBJECT_ID('dbo.PendingRegistrations', 'U') IS NOT NULL THEN 1 ELSE 0 END"),
    ];

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

            foreach (var expectation in SchemaExpectations)
            {
                command.CommandText = expectation.Sql;
                var shortfall = SchemaShortfall(expectation, await command.ExecuteScalarAsync(timeout.Token));
                if (shortfall is { } degraded)
                {
                    return degraded;
                }
            }

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

    /// <summary>
    /// Turns one expectation's answer into the Degraded result, or null when
    /// the schema is there. Separate from the probe so the interpretation is
    /// testable without a database.
    /// </summary>
    /// <remarks>
    /// /ready answers without the gateway token, so the public description says
    /// only that the schema is behind. Which object is missing rides on the
    /// exception, which the JSON formatter emits only when
    /// HealthChecks:ExposeErrorDetails is on and the health service logs at
    /// Warning either way — the operator reads it there, the anonymous prober
    /// does not.
    /// </remarks>
    public static HealthCheckResult? SchemaShortfall(SchemaExpectation expectation, object? answer)
    {
        var present = answer is int i ? i == 1 : answer is long l && l == 1;
        return present
            ? null
            : HealthCheckResult.Degraded(
                "Database schema is behind this build. Publish Auth_DB before this API.",
                new InvalidOperationException($"{expectation.Name} is missing."));
    }
}
