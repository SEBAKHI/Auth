namespace Auth.Domain.ValueObjects;

/// <summary>
/// Which qualifying second factors an account holds (FA2): an enabled authenticator app,
/// and a live, user-verified passkey of the current relying party. The emailed code never
/// qualifies, and neither does a passkey bound to another RP ID or one removed.
/// </summary>
/// <remarks>
/// Read in one statement, from one definition of each factor
/// (<c>TwoFactorStateStore</c>), by every policy that asks whether the account has a
/// second factor. <c>default</c> is "none": an account with neither.
/// </remarks>
/// <param name="Totp">The authenticator app is enabled.</param>
/// <param name="Passkey">A live, user-verified passkey of the current RP ID exists.</param>
public readonly record struct QualifyingSecondFactors(bool Totp, bool Passkey)
{
    /// <summary>No qualifying factor at all.</summary>
    public static QualifyingSecondFactors None => default;

    /// <summary>Gets whether the account holds any qualifying factor.</summary>
    public bool HasAny => Totp || Passkey;

    /// <summary>
    /// Gets whether a passkey is the account's only qualifying factor: then only a passkey
    /// sign-in proves two factors for it, since there is no app to step up with.
    /// </summary>
    public bool IsPasskeyOnly => Passkey && !Totp;
}
