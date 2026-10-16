namespace Auth.Domain.Enums;

/// <summary>
/// What an application key may authenticate (OI-171, owner decision D-171-2 (a)): one purpose per
/// key, so a key that sits on an internet-facing web server cannot also obtain machine tokens,
/// and a batch job's key cannot redeem a person's sign-in. Stored as its number.
/// </summary>
public enum MachineKeyPurpose
{
    /// <summary>
    /// The <c>client_credentials</c> grant: the application's own server obtains a machine token.
    /// Every key created before purposes existed is this one.
    /// </summary>
    Machine = 1,

    /// <summary>
    /// The <c>authorization_code</c> and <c>refresh_token</c> grants of a server-side web
    /// application (a Backend-for-Frontend): the server proves who it is while it redeems a
    /// person's code and renews that person's tokens.
    /// </summary>
    SignIn = 2
}
