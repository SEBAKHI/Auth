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

    /// <summary>The shortest lifetime of an emailed second-factor code the setting accepts, in minutes.</summary>
    public const int MinEmailCodeLifetimeMinutes = 5;

    /// <summary>
    /// The longest lifetime of an emailed second-factor code the setting accepts, in
    /// minutes: the bound the keyring keeps an old key's codes verifiable for
    /// (<c>CredentialPurposePolicy</c>, TwoFactorEmailCode), and within NIST SP
    /// 800-63B §3.1.3.2's ten minutes.
    /// </summary>
    public const int MaxEmailCodeLifetimeMinutes = 10;

    /// <summary>
    /// Gets or sets how recent a sign-in must be, in minutes, before the session
    /// may set up, switch on or switch off two-factor authentication. An older
    /// session is asked to sign in again first, so a session left open — or a
    /// stolen token — cannot change the second factor. Read per request; values
    /// outside 5–60 are brought inside it, so no value turns the check off.
    /// </summary>
    public int ReauthenticationMaxAgeMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets whether a platform administrator — anyone whose platform token
    /// would carry at least one permission — must prove a second factor in the
    /// session before that token carries platform permissions and roles. Until the
    /// session has, the token carries neither, names what is missing in
    /// <c>mfa_req</c>, and the consoles open the two-step page. Application tokens,
    /// organization permissions and accounts without platform permissions are
    /// never affected. Read at every sign-in and refresh, so it is hot. Off by
    /// default: switched on only once every administrator has a second factor. A
    /// rollout switch; a later commit removes it and keeps enforcement on.
    /// </summary>
    public bool EnforceForPlatformAdmins { get; set; }

    /// <summary>
    /// Gets or sets how long a code emailed as the second factor is accepted, in
    /// minutes, from the moment it is sent (S24). Each send — the first and every
    /// resend — sets the challenge to expire this long after it, so a delivery that
    /// takes a few minutes still leaves time to enter the code. Read per send; values
    /// outside 5–10 are brought inside it. Keep it at or above how long the
    /// deployment's mail takes to arrive.
    /// </summary>
    public int EmailCodeLifetimeMinutes { get; set; } = MaxEmailCodeLifetimeMinutes;

    /// <summary>
    /// <see cref="EmailCodeLifetimeMinutes"/> brought inside 5–10, so no stored value
    /// makes a code outlive the keyring's bound or expire before it can arrive.
    /// </summary>
    public int EffectiveEmailCodeLifetimeMinutes =>
        Math.Clamp(EmailCodeLifetimeMinutes, MinEmailCodeLifetimeMinutes, MaxEmailCodeLifetimeMinutes);
}
