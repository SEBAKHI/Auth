namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Aggregated session and refresh-token hygiene statistics over a trailing window of days.
/// </summary>
public sealed record SessionStatsSnapshot
{
    /// <summary>Sessions not ended and not yet expired.</summary>
    public required int ActiveSessions { get; init; }

    /// <summary>Sessions never ended but already past their expiry (stale-open).</summary>
    public required int StaleOpenSessions { get; init; }

    /// <summary>Sessions started inside the window.</summary>
    public required int StartedInWindow { get; init; }

    /// <summary>Sessions ended inside the window grouped by end reason.</summary>
    public required IReadOnlyList<ReasonCount> EndReasons { get; init; }

    /// <summary>Average duration in minutes of sessions that ended inside the window; null when none ended.</summary>
    public required double? AverageSessionMinutes { get; init; }

    /// <summary>Refresh tokens neither revoked nor expired.</summary>
    public required int ActiveRefreshTokens { get; init; }

    /// <summary>Refresh tokens revoked inside the window.</summary>
    public required int TokensRevokedInWindow { get; init; }

    /// <summary>Refresh tokens revoked inside the window grouped by revocation reason.</summary>
    public required IReadOnlyList<ReasonCount> RevocationReasons { get; init; }
}
