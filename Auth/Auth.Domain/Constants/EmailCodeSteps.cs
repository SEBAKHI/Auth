namespace Auth.Domain.Constants;

/// <summary>
/// The wire names of what a sign-in by emailed code does next once its code matched
/// (OI-215). Part of the public contract: never renamed.
/// </summary>
/// <remarks>
/// Only ever answered after the code was checked: before it, every address gets the
/// same answer, and these say what the account is, which only the mailbox's holder may
/// learn.
/// </remarks>
public static class EmailCodeSteps
{
    /// <summary>The address has an account: the sign-in step finishes it.</summary>
    public const string SignIn = "signIn";

    /// <summary>
    /// The address's account waits for deletion: the recovery step brings it back and
    /// signs it in.
    /// </summary>
    public const string Recover = "recover";

    /// <summary>
    /// The address has no account and sign-up is open: the registration's completion
    /// step creates it, with the fields the settings ask for.
    /// </summary>
    public const string CompleteAccount = "completeAccount";

    /// <summary>
    /// The account has a password and this sign-in needs it (it holds platform
    /// authority, or the page asked for full proof): the ordinary password sign-in
    /// continues it. No session was issued.
    /// </summary>
    public const string PasswordRequired = "passwordRequired";
}
