using System.ComponentModel.DataAnnotations;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// Configuration section <c>ErrorContract:Outage</c>.
/// </summary>
public sealed class OutageOptions
{
    public const string SectionName = "ErrorContract:Outage";

    /// <summary>
    /// The <c>Retry-After</c> a 503 carries when whoever returned it set none, in seconds.
    /// </summary>
    [Range(1, 3600)]
    public int RetryAfterSeconds { get; set; } = 30;
}
