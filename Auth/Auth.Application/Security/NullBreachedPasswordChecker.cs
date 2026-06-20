using Auth.Application.Interfaces;

namespace Auth.Application.Security;

/// <summary>
/// No-op breached-password checker used when the feature is disabled
/// (<c>Password:BreachedPasswordCheck:Enabled = false</c>). Makes no external calls and always
/// reports the password as not breached, so the feature has zero runtime footprint when off.
/// </summary>
public sealed class NullBreachedPasswordChecker : IBreachedPasswordChecker
{
    /// <inheritdoc />
    public Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken)
        => Task.FromResult(0);
}
