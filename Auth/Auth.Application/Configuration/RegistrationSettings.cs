namespace Auth.Application.Configuration;

/// <summary>
/// Who may bring a new account into existence without an administrator.
/// <para>
/// Two doors create accounts for strangers, and an operator who wants neither
/// has to be able to shut both: the public sign-up endpoint, and the first
/// sign-in of a provider identity that matches no account here. They are
/// separate switches because "no passwords, sign in with Google only" is a
/// real policy, and so is its opposite.
/// </para>
/// <para>
/// Neither switch touches the paths where somebody already decided this person
/// belongs: an administrator creating a user, or a registration that redeems an
/// organization invitation.
/// </para>
/// </summary>
public class RegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>
    /// Gets or sets whether anyone may create an account through the public
    /// sign-up endpoint. Open by default, which is the behaviour every
    /// deployment had before this switch existed; closing it turns the endpoint
    /// into a refusal that costs no password hash, no row and no email.
    /// </summary>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a provider identity (Google, Apple) that matches no
    /// local account may create one on its first sign-in. Closing this does not
    /// disturb accounts that already exist: linking a provider to an account
    /// with the same address, and signing in with one already linked, both
    /// continue to work.
    /// </summary>
    public bool AllowExternalProvisioning { get; set; } = true;
}
