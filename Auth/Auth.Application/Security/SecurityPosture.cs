using Microsoft.Extensions.Logging;

namespace Auth.Application.Security;

/// <summary>Whether an area of the security posture is as strong as it ships.</summary>
public enum SecurityPostureLevel
{
    /// <summary>Nothing to report beyond the configuration in force.</summary>
    Ok = 0,

    /// <summary>The area is weaker than it ships, or a dependency it needs does not answer.</summary>
    Weak = 1
}

/// <summary>
/// The outcome of a contributor's call to a dependency it needs, such as a lookup of a value the
/// dependency is known to hold.
/// </summary>
/// <param name="Outcome"><c>Reachable</c>, or the kind of failure (for example <c>Timeout</c>).</param>
/// <param name="ElapsedMs">How long the call took. Never part of <see cref="SecurityPosture.State"/>.</param>
public sealed record SecurityPostureProbe(string Outcome, long ElapsedMs)
{
    /// <summary>The outcome of a call that answered as expected.</summary>
    public const string Reachable = "Reachable";

    /// <summary>Whether the call answered as expected.</summary>
    public bool Succeeded => Outcome == Reachable;
}

/// <summary>One log line the posture check writes for a contributor, as a message template.</summary>
/// <param name="Level">The level to write it at.</param>
/// <param name="MessageTemplate">A structured-logging template, constant per kind of line.</param>
/// <param name="Arguments">The template's values, in placeholder order.</param>
/// <param name="Exception">The failure behind the line, when there is one, written with it.</param>
public sealed record SecurityPostureLine(
    LogLevel Level,
    string MessageTemplate,
    IReadOnlyList<object?> Arguments,
    Exception? Exception = null);

/// <summary>What a contributor reports about its area.</summary>
/// <param name="Level">Ok, or Weak with <paramref name="Reasons"/>.</param>
/// <param name="Reasons">Why the area is weak; empty when it is Ok.</param>
/// <param name="Lines">The lines that describe the area, in the order they are written.</param>
/// <param name="State">
/// What identifies this posture: two evaluations with the same state are the same posture, and
/// the second writes nothing. It leaves out whatever varies between identical states, such as an
/// elapsed time, so a reload that changes nothing in the area stays silent.
/// </param>
/// <param name="Probe">The dependency call, when the area made one.</param>
public sealed record SecurityPosture(
    SecurityPostureLevel Level,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<SecurityPostureLine> Lines,
    string State,
    SecurityPostureProbe? Probe = null);
