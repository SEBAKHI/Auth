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
/// A third door redeems an organization invitation. It used to be described here
/// as a path where "somebody already decided this person belongs", and left
/// uncovered on that basis. The description was wrong: the somebody was any
/// signed-in user, because creating an organization required no permission and
/// its owner may invite any address. That is now two separate controls — who may
/// create an organization at all, and this switch — and the invitation's own
/// premise was repaired by keeping the token out of the inviter's hands.
/// </para>
/// <para>
/// Still untouched by any of them: an administrator creating a user. That path
/// has a named, authenticated, permission-checked actor and an audit row naming
/// them, which is what the other three did not.
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

    /// <summary>
    /// Gets or sets whether an account may be created by redeeming an
    /// organization invitation. Open by default, because closing it stops a
    /// normal onboarding flow that most deployments want.
    /// <para>
    /// Separate from <see cref="AllowSelfRegistration"/> on purpose. An operator
    /// who closes public sign-up has usually decided that accounts arrive by
    /// invitation instead, so folding the two together would break the very
    /// workflow that closing the first one implies. An operator who wants
    /// accounts to exist ONLY when an administrator creates them closes all
    /// three.
    /// </para>
    /// <para>
    /// Closing this does not cancel invitations already sent, and does not stop
    /// an invited person who ALREADY has an account from accepting one: that path
    /// adds a membership to an existing account rather than creating one, which
    /// is not what any of these switches govern.
    /// </para>
    /// </summary>
    public bool AllowInvitationRegistration { get; set; } = true;
}
