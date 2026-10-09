namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Lets <c>PlatformMfa.EnforcedSettingsUnavailable</c> be written once per process.
/// </summary>
/// <remarks>
/// The window that warning reports opens once, when the process starts, and closes
/// for good at the first successful settings load (a later failed refresh keeps
/// the values last read), so once per process is once per window. Registered as a
/// singleton: <see cref="PlatformMfaPolicy"/>, which writes the warning, lives per
/// request.
/// </remarks>
public sealed class EnforcedSettingsWarning
{
    private int _claimed;

    /// <summary>True for the first caller only.</summary>
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
