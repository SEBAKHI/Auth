namespace Auth.Application.Configuration;

/// <summary>
/// Clamps the lock settings after binding, so every consumer reads values the
/// lock rules can hold, whatever a configuration file says. The console's
/// validator refuses an out-of-range value from the settings page; a file is
/// bound with no validation at all, and a file with
/// <c>Password:MaxFailedAttempts</c> above the limit would otherwise make the
/// timed lock unreachable.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>ConsecutiveFailureLimit</c> (Hard′) is clamped to 10 through 100: never
/// above NIST SP 800-63B-4 §3.2.2's 100, and never so low that a forgetful owner
/// reaches it in an afternoon.</item>
/// <item><c>MaxFailedAttempts</c> (Max′) is clamped to 1 through Hard′ − 1, so a
/// timed lock always comes before the hard one: the failure statement tests the
/// hard limit first, so at Max′ = Hard′ the first lock would be the hard one, and
/// the per-address ceiling would silently become Hard′. The console refuses such a
/// save (<c>SystemSettingsValueValidator</c>, section Password).</item>
/// <item><c>LockoutDurationMinutes</c> is clamped to 1 through 1440 (the console's
/// range): at 0 a timed lock would lapse at once, and the per-address ceiling, the
/// only bound on an account pending deletion, would count an empty window.</item>
/// <item><c>ConsecutiveFailureMode</c> outside the enum (a file or an environment
/// variable may bind <c>2</c>) reads as Enforce: every consumer tests one named
/// value, so an undefined one would be half Legacy, silently and with no audit
/// row.</item>
/// </list>
/// Applied through <c>PostConfigure</c>, so it re-runs on every rebind and holds for
/// <c>IOptionsSnapshot</c> and <c>IOptionsMonitor</c> consumers after a settings change.
/// </remarks>
public static class PasswordSettingsNormalizer
{
    /// <summary>The lowest consecutive-failure limit the rules accept.</summary>
    public const int MinimumConsecutiveFailureLimit = 10;

    /// <summary>The highest consecutive-failure limit NIST SP 800-63B-4 allows.</summary>
    public const int MaximumConsecutiveFailureLimit = 100;

    /// <summary>
    /// Clamps the lock settings in place. Registered as a <c>PostConfigure</c> action.
    /// </summary>
    public static void Apply(PasswordSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ConsecutiveFailureLimit = Math.Clamp(
            settings.ConsecutiveFailureLimit, MinimumConsecutiveFailureLimit, MaximumConsecutiveFailureLimit);
        settings.MaxFailedAttempts = Math.Clamp(settings.MaxFailedAttempts, 1, settings.ConsecutiveFailureLimit - 1);
        settings.LockoutDurationMinutes = Math.Clamp(settings.LockoutDurationMinutes, 1, 1440);
        if (!Enum.IsDefined(settings.ConsecutiveFailureMode))
        {
            settings.ConsecutiveFailureMode = ConsecutiveFailureMode.Enforce;
        }
    }
}
