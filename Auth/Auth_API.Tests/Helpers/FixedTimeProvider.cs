namespace Auth_API.Tests.Helpers;

/// <summary>
/// A clock that always reads the same instant, so a test can say exactly which
/// TOTP time step "now" falls in. Written by hand rather than taken from a
/// time-testing package: overriding <see cref="TimeProvider.GetUtcNow"/> is all a
/// code check needs.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
