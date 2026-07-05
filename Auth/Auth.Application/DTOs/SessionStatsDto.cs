namespace Auth.Application.DTOs;

/// <summary>
/// Dashboard session and refresh-token hygiene statistics over a trailing window of days.
/// </summary>
public class SessionStatsDto
{
    public int Days { get; set; }
    public int ActiveSessions { get; set; }
    public int StaleOpenSessions { get; set; }
    public int StartedInWindow { get; set; }
    public List<ReasonCountDto> EndReasons { get; set; } = [];
    public double? AverageSessionMinutes { get; set; }
    public int ActiveRefreshTokens { get; set; }
    public int TokensRevokedInWindow { get; set; }
    public List<ReasonCountDto> RevocationReasons { get; set; } = [];
    public int TokensExpiringIn7Days { get; set; }
}
