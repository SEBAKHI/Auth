using Auth.Application.Security;

namespace Auth.Application.Interfaces;

/// <summary>
/// One area of the deployment's security posture (a control's configuration, and whether a
/// dependency it needs answers). The posture check collects every registered contributor, runs
/// each at startup and after every configuration reload, and writes its lines when its state
/// differs from the last one written.
/// </summary>
/// <remarks>
/// A contributor does not log: it describes, and the posture check writes. It never runs on a
/// request path and never delays startup, so it may call out (a probe) under its own deadline.
/// An exception it throws costs one Error line and nothing else.
/// </remarks>
public interface ISecurityPostureContributor
{
    /// <summary>A stable, lowercase name for the area, such as <c>password.breach-check</c>.</summary>
    string Name { get; }

    /// <summary>Evaluates the area against the configuration in force now.</summary>
    /// <param name="cancellationToken">The host's stopping token.</param>
    Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken);
}
