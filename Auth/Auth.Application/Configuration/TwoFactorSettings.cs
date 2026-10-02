namespace Auth.Application.Configuration;

/// <summary>
/// Configuration settings for two-factor authentication.
/// </summary>
public class TwoFactorSettings
{
    public const string SectionName = "TwoFactor";

    /// <summary>
    /// Gets or sets whether an authenticator-app code is accepted only once.
    /// True refuses a code whose time step is not newer than the last one the
    /// account accepted, on sign-in, account recovery and switching two-factor
    /// off. A rollout switch, read per request: false is an incident lever that
    /// lets reused codes through again, each one logged as a warning.
    /// </summary>
    public bool RejectReusedCodes { get; set; } = true;
}
