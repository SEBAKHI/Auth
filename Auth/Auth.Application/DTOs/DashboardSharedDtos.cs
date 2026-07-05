namespace Auth.Application.DTOs;

/// <summary>
/// Count of occurrences on a single UTC calendar day.
/// </summary>
public class DailyCountDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Count of occurrences for a categorical reason.
/// </summary>
public class ReasonCountDto
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
}
