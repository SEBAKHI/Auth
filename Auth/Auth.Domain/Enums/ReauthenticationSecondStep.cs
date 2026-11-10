namespace Auth.Domain.Enums;

/// <summary>
/// What must follow the first proof when a signed-in person proves themself again: the second
/// step a sign-in of the same account would ask for.
/// </summary>
public enum ReauthenticationSecondStep
{
    /// <summary>Nothing: the account has no second step, or its first proof already covers it.</summary>
    None = 0,

    /// <summary>A code from the authenticator app, or a recovery code.</summary>
    AuthenticatorApp = 1,

    /// <summary>A code emailed for this proof, or a recovery code.</summary>
    EmailCode = 2,

    /// <summary>A recovery code: the account's second step is the emailed code, and email is off.</summary>
    RecoveryCodeOnly = 3
}
