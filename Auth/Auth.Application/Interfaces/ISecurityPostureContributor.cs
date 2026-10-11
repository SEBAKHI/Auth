using Auth.Application.Security;

namespace Auth.Application.Interfaces;

/// <summary>
/// One area of the deployment's security posture (a control's configuration, and whether a
/// dependency it needs answers). The posture check collects every registered contributor, runs
/// each at startup and after every configuration reload, and writes its lines when its state
/// differs from the last one written.
/// </summary>
/// <remarks>
/// <para>The contract every contributor keeps:</para>
/// <list type="bullet">
/// <item>It is registered as a singleton, and resolves any scoped service through a scope of its
/// own, created per evaluation (an <c>IServiceScopeFactory</c>), never captured.</item>
/// <item>It does not log: it describes, and the posture check writes.</item>
/// <item>It honours the token it is given (the host's stopping token), so a stop ends it.</item>
/// <item>It may call out (a probe) under a deadline of its own: it never runs on a request path,
/// never delays startup, and runs on a thread of its own beside the other contributors.</item>
/// </list>
/// <para>An exception it throws costs one Error line and nothing else.</para>
/// </remarks>
public interface ISecurityPostureContributor
{
    /// <summary>A stable, lowercase name for the area, such as <c>password.breach-check</c>.</summary>
    string Name { get; }

    /// <summary>Evaluates the area against the configuration in force now.</summary>
    /// <param name="cancellationToken">The host's stopping token.</param>
    Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken);
}
