using Serilog.Core;
using Serilog.Events;

namespace Auth_API.Common.Logging;

/// <summary>
/// Holds the Serilog level switches (default + per-namespace overrides) that
/// keep minimum levels hot: the logger pipeline is built once at startup,
/// but levels routed through these switches follow configuration changes —
/// including saved system-settings overrides. Sinks and enrichers stay
/// startup-bound by design.
/// </summary>
public sealed class LoggingLevelSwitchRegistry
{
    /// <summary>
    /// Override namespaces controlled from system settings. Kept in sync
    /// with the Serilog section of the settings registry.
    /// </summary>
    public static readonly string[] OverrideNamespaces =
        ["Microsoft", "Microsoft.Hosting.Lifetime", "System"];

    public LoggingLevelSwitch Default { get; } = new(LogEventLevel.Information);

    public IReadOnlyDictionary<string, LoggingLevelSwitch> Overrides { get; } =
        OverrideNamespaces.ToDictionary(ns => ns, _ => new LoggingLevelSwitch(LogEventLevel.Information));

    /// <summary>
    /// Re-reads the Serilog minimum levels from the live configuration and
    /// applies them to the switches. Unparsable/absent values keep the
    /// current level for overrides and fall back to Information for Default.
    /// </summary>
    public void ApplyFrom(IConfiguration configuration)
    {
        Default.MinimumLevel = Parse(
            configuration["Serilog:MinimumLevel:Default"], LogEventLevel.Information);

        foreach (var (ns, levelSwitch) in Overrides)
        {
            levelSwitch.MinimumLevel = Parse(
                configuration[$"Serilog:MinimumLevel:Override:{ns}"], levelSwitch.MinimumLevel);
        }
    }

    private static LogEventLevel Parse(string? text, LogEventLevel fallback)
        => Enum.TryParse<LogEventLevel>(text, ignoreCase: true, out var level) ? level : fallback;
}
