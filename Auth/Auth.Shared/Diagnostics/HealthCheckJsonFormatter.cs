using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth.Shared.Diagnostics;

/// <summary>
/// Serializes an ASP.NET Core <see cref="HealthReport"/> into a detailed JSON payload for the
/// <c>/health</c> and <c>/ready</c> endpoints. Shared by the Auth API and the API Gateway so both
/// expose an identical, diagnosable response shape (overall status plus a breakdown per check).
/// </summary>
/// <remarks>
/// This type deliberately returns a <see cref="string"/> rather than writing to an
/// <c>HttpContext</c>, so <c>Auth.Shared</c> does not need to reference the ASP.NET Core web
/// framework (it is also consumed by non-web layers). Each web app supplies the three lines of
/// HTTP glue when wiring its <c>ResponseWriter</c>.
/// </remarks>
public static class HealthCheckJsonFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Builds the JSON body for a health report.
    /// </summary>
    /// <param name="report">The aggregated health report produced by the health-check service.</param>
    /// <param name="includeErrorDetails">
    /// When <c>true</c>, includes each failed check's exception message. Keep this <c>false</c> on
    /// publicly reachable endpoints in production to avoid leaking internal details (connection
    /// strings, server names, stack traces); enable it only briefly for diagnosis. The full
    /// exception is always written to the server logs regardless of this flag.
    /// </param>
    /// <returns>A compact camelCase JSON string describing the overall status and each check.</returns>
    public static string Serialize(HealthReport report, bool includeErrorDetails)
    {
        ArgumentNullException.ThrowIfNull(report);

        var payload = new HealthPayload(
            Status: report.Status.ToString(),
            TotalDurationMs: Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            Checks: report.Entries
                .Select(entry => new HealthCheckPayload(
                    Name: entry.Key,
                    Status: entry.Value.Status.ToString(),
                    DurationMs: Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                    Description: entry.Value.Description,
                    Tags: entry.Value.Tags.Any() ? entry.Value.Tags.ToArray() : null,
                    Error: includeErrorDetails ? entry.Value.Exception?.Message : null))
                .ToArray());

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private sealed record HealthPayload(
        string Status,
        double TotalDurationMs,
        IReadOnlyCollection<HealthCheckPayload> Checks);

    private sealed record HealthCheckPayload(
        string Name,
        string Status,
        double DurationMs,
        string? Description,
        IReadOnlyCollection<string>? Tags,
        string? Error);
}
