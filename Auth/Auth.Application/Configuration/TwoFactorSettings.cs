namespace Auth.Application.Configuration;

/// <summary>
/// Configuration settings for two-factor authentication.
/// </summary>
public class TwoFactorSettings
{
    public const string SectionName = "TwoFactor";

    /// <summary>The shortest re-authentication window the setting accepts, in minutes.</summary>
    public const int MinReauthenticationMaxAgeMinutes = 5;

    /// <summary>The longest re-authentication window the setting accepts, in minutes.</summary>
    public const int MaxReauthenticationMaxAgeMinutes = 60;

    /// <summary>
    /// Gets or sets whether an authenticator-app code is accepted only once.
    /// True refuses a code whose time step is not newer than the last one the
    /// account accepted, on sign-in, account recovery and switching two-factor
    /// on or off. A rollout switch, read per request: false is an incident lever
    /// that lets reused codes through again, each one logged as a warning.
    /// </summary>
    public bool RejectReusedCodes { get; set; } = true;

    /// <summary>
    /// Gets or sets how recent a sign-in must be, in minutes, before the session
    /// may set up, switch on or switch off two-factor authentication. An older
    /// session is asked to sign in again first, so a session left open — or a
    /// stolen token — cannot change the second factor. Read per request; values
    /// outside 5–60 are brought inside it, so no value turns the check off.
    /// </summary>
    public int ReauthenticationMaxAgeMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets whether an account binding its FIRST second factor must also
    /// enter a code emailed to its confirmed address, so that whoever holds only
    /// the password cannot bind an authenticator of their own. Applies only while
    /// <c>Email:Enabled</c> is true: with email off nothing could deliver the code,
    /// and the factor binds with the notice alone. A rollout switch, read per
    /// request; a later commit removes it together with its false branch.
    /// </summary>
    public bool RequireEmailCodeForFirstFactor { get; set; } = true;
}
