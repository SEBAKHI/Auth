namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Number of occurrences on a single UTC calendar day.
/// </summary>
public sealed record DailyCount(DateTime Date, int Count);

/// <summary>
/// Number of occurrences for a categorical reason (e.g. login failure reason,
/// session end reason, token revocation reason).
/// </summary>
public sealed record ReasonCount(string Reason, int Count);
