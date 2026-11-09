using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// What a signed-in account must prove to prove itself again, with no new sign-in: exactly what a
/// sign-in of the same account would ask for, never less. The one rule, read by the window that
/// asks and by the request that answers it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A live passkey the account can use: alone, whatever else it holds (M3).</item>
/// <item>A password, on a confirmed address: a password set on an address nobody proved may be a
/// stranger's (D-215-5), so it proves nothing. Then the second step the sign-in would ask for.</item>
/// <item>No password and email on, on a confirmed address: the emailed code, its first factor
/// (D-215-1). An account whose second step is that same mailbox is asked for it once, as a sign-in
/// by code is. An authenticator app still follows.</item>
/// <item>An administrator whose only usable second factor is a passkey: that passkey alone
/// (D-S20-4 (ii-s)).</item>
/// <item>An account whose flag sends a sign-in to a second step that no factor row can answer: only
/// a passkey, which proves both alone. A sign-in stops at that step, so a password here would be
/// less than a sign-in asks.</item>
/// </list>
/// An account none of these fits (only an external provider, and email off) has nothing to prove in
/// place: <see cref="CanProveInPlace"/> is false, and its way forward is a sign-in.
/// </remarks>
public sealed record class ReauthenticationRequirement
{
    private ReauthenticationRequirement(
        bool passkey,
        bool password,
        bool emailCode,
        ReauthenticationSecondStep secondStep)
    {
        Passkey = passkey;
        Password = password;
        EmailCode = emailCode;
        SecondStep = secondStep;
    }

    /// <summary>Gets whether a passkey may prove the person, alone.</summary>
    public bool Passkey { get; }

    /// <summary>Gets whether the password is the first proof.</summary>
    public bool Password { get; }

    /// <summary>Gets whether an emailed code is the first proof.</summary>
    public bool EmailCode { get; }

    /// <summary>Gets what must follow the password or the emailed code.</summary>
    public ReauthenticationSecondStep SecondStep { get; }

    /// <summary>Gets whether the person can prove themself at all without signing in.</summary>
    public bool CanProveInPlace => Passkey || Password || EmailCode;

    /// <summary>Gets whether a code can be asked for by email: as the first proof, or the second step.</summary>
    public bool SendsEmailCode => EmailCode || SecondStep == ReauthenticationSecondStep.EmailCode;

    /// <summary>Reads what an account must prove from its state.</summary>
    /// <param name="hasPassword">The account has a password.</param>
    /// <param name="emailConfirmed">The account's address was proved.</param>
    /// <param name="factor">The account's second-factor row; null when it has none.</param>
    /// <param name="emailOn">Email sending is on (<c>Email:Enabled</c>).</param>
    /// <param name="passkeyUsable">A live passkey counts right now (D-150-4).</param>
    /// <param name="passkeyOnlyAdministrator">The account administers the platform and its only usable factor is a passkey.</param>
    /// <param name="signInGateOn">The account flag the sign-in gate reads: on, a sign-in always goes on to a second step.</param>
    public static ReauthenticationRequirement For(
        bool hasPassword,
        bool emailConfirmed,
        TwoFactorSnapshot? factor,
        bool emailOn,
        bool passkeyUsable,
        bool passkeyOnlyAdministrator,
        bool signInGateOn)
    {
        if (passkeyOnlyAdministrator)
        {
            return new ReauthenticationRequirement(true, false, false, ReauthenticationSecondStep.None);
        }

        var gateHasNoAnswer = signInGateOn && factor?.IsEnabled != true && factor?.HasEmailCode != true;
        if (!emailConfirmed || gateHasNoAnswer)
        {
            return new ReauthenticationRequirement(passkeyUsable, false, false, ReauthenticationSecondStep.None);
        }

        if (hasPassword)
        {
            return new ReauthenticationRequirement(passkeyUsable, true, false, SecondStepAfterPassword(factor, emailOn));
        }

        return new ReauthenticationRequirement(
            passkeyUsable,
            false,
            emailOn,
            emailOn && factor?.IsEnabled == true ? ReauthenticationSecondStep.AuthenticatorApp : ReauthenticationSecondStep.None);
    }

    private static ReauthenticationSecondStep SecondStepAfterPassword(TwoFactorSnapshot? factor, bool emailOn)
    {
        if (factor?.IsEnabled == true)
        {
            return ReauthenticationSecondStep.AuthenticatorApp;
        }

        if (factor?.HasEmailCode == true)
        {
            return emailOn ? ReauthenticationSecondStep.EmailCode : ReauthenticationSecondStep.RecoveryCodeOnly;
        }

        return ReauthenticationSecondStep.None;
    }
}
