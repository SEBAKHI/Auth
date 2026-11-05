using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The authentication methods one sign-in session has proved, as a set of flags.
/// Recorded on the session rows (<c>UserSessions.AuthMethods</c>,
/// <c>IdpSessions.AuthMethods</c>) and on a two-factor challenge
/// (<c>TwoFactorChallenges.PrimaryMethod</c>), and carried into the access token
/// as <c>amr</c> and <c>auth_time</c>.
/// </summary>
/// <remarks>
/// <para>
/// Persisted as an <c>INT</c> of these fixed bits, which are never renumbered:
/// Password = 1, ExternalIdentity = 2, EmailCode = 4, Totp = 8, RecoveryCode = 16,
/// Passkey = 32, PasskeyUserVerified = 64, EmailCodeFirstFactor = 128.
/// <see cref="Unknown"/> (no bit) is stored as <c>NULL</c>: a session recorded
/// before the column existed, or one whose row could not be written, proved nothing
/// anyone can read back.
/// </para>
/// <para>
/// An emailed code proves a mailbox. For an account that has a password it is
/// recorded but is never a factor of either kind: whoever holds the mailbox already
/// holds the password reset. The same bit records it whether it opened the sign-in
/// (a verify-email link, or a sign-in by code) or completed one as the second step
/// of an account that chose the emailed code (S24).
/// </para>
/// <para>
/// For an account that has NO password the emailed code is its first factor (OI-215,
/// owner decision D-215-1 (b)): nothing else proves who the person is, and without it
/// such an account could never meet a policy that asks for two factors. That is
/// <see cref="EmailCodeFirstFactor"/>, recorded beside <see cref="EmailCode"/> only
/// where a sign-in's first proof was the code and the account had no password at
/// that moment. It is decided at sign-in and kept with the session, because the
/// account may add a password later. A build that predates the bit masks it away
/// and reads such a session as the emailed code alone. A recovery code issued
/// without two factors (FA3) records no bit at all.
/// </para>
/// <para>
/// A passkey (S20) is a first factor of its own (D-T14 A), never a step after the
/// password: <see cref="Passkey"/> is a primary. Every passkey is made with the device's own
/// check of the person (fingerprint, face or PIN), and a sign-in the device verified records
/// <see cref="PasskeyUserVerified"/> beside it, which proves two factors alone (M3):
/// something the person has, and something they are or know. Nothing issues either bit
/// before passkey sign-in ships (S20 PR B).
/// </para>
/// </remarks>
public readonly record struct AuthenticationMethods
{
    private const int PasswordBit = 1;
    private const int ExternalIdentityBit = 2;
    private const int EmailCodeBit = 4;
    private const int TotpBit = 8;
    private const int RecoveryCodeBit = 16;
    private const int PasskeyBit = 32;
    private const int PasskeyUserVerifiedBit = 64;
    private const int EmailCodeFirstFactorBit = 128;

    // A stored value carrying any other bit was not written by this code: it is
    // read as what it holds of the known bits, never trusted beyond them.
    private const int KnownBits =
        PasswordBit | ExternalIdentityBit | EmailCodeBit | TotpBit | RecoveryCodeBit
        | PasskeyBit | PasskeyUserVerifiedBit | EmailCodeFirstFactorBit;

    private const int PrimaryBits = PasswordBit | ExternalIdentityBit | PasskeyBit | EmailCodeFirstFactorBit;
    private const int SecondFactorBits = TotpBit | RecoveryCodeBit;

    // RFC 8176 values. The password, the authenticator app and the passkey have one;
    // an external identity, an emailed code (as either factor) and a recovery code
    // are reflected by "mfa" alone. An emailed code is never "otp": that value
    // means HOTP/TOTP. A passkey is "pop" (proof of possession of a key, IANA
    // 2023-05-08) and, verified by the device, "user" (D-T18 C): never "hwk" or
    // "swk", since attestation none cannot tell a hardware key from a synced one.
    private const string PasswordAmr = "pwd";
    private const string ProofOfPossessionAmr = "pop";
    private const string UserPresenceAmr = "user";
    private const string OneTimePasswordAmr = "otp";
    private const string MultiFactorAmr = "mfa";

    private static readonly FrozenDictionary<SecondFactorMethod, AuthenticationMethods> SecondFactors =
        new Dictionary<SecondFactorMethod, AuthenticationMethods>
        {
            [SecondFactorMethod.Totp] = new(TotpBit),
            [SecondFactorMethod.RecoveryCode] = new(RecoveryCodeBit),
            // Recorded, never counted: IsMfaSatisfied reads Totp and RecoveryCode only.
            [SecondFactorMethod.EmailCode] = new(EmailCodeBit),
        }.ToFrozenDictionary();

    private AuthenticationMethods(int value)
    {
        Value = value & KnownBits;
    }

    /// <summary>Nothing recorded. Stored as <c>NULL</c>.</summary>
    public static AuthenticationMethods Unknown { get; } = new(0);

    /// <summary>The account's password.</summary>
    public static AuthenticationMethods Password { get; } = new(PasswordBit);

    /// <summary>An external identity provider (Google, Apple) vouched for the person.</summary>
    public static AuthenticationMethods ExternalIdentity { get; } = new(ExternalIdentityBit);

    /// <summary>
    /// A code sent to the account's email address, as the sign-in's first proof or as
    /// the second step of an account that chose it. Never a factor on its own: for an
    /// account without a password it is recorded with <see cref="EmailCodeFirstFactor"/>.
    /// </summary>
    public static AuthenticationMethods EmailCode { get; } = new(EmailCodeBit);

    /// <summary>
    /// The emailed code as the first factor of an account that has no password
    /// (D-215-1). Recorded with <see cref="EmailCode"/>, never alone. It never proves
    /// a password and has no <c>amr</c> value of its own.
    /// </summary>
    public static AuthenticationMethods EmailCodeFirstFactor { get; } = new(EmailCodeFirstFactorBit);

    /// <summary>A code from the account's authenticator app.</summary>
    public static AuthenticationMethods Totp { get; } = new(TotpBit);

    /// <summary>One of the account's single-use recovery codes.</summary>
    public static AuthenticationMethods RecoveryCode { get; } = new(RecoveryCodeBit);

    /// <summary>
    /// A passkey of the account (S20): a first factor of its own, the proof that the
    /// person holds the device. Never a password.
    /// </summary>
    public static AuthenticationMethods Passkey { get; } = new(PasskeyBit);

    /// <summary>
    /// The device verified the person before the passkey signed (fingerprint, face or PIN):
    /// with <see cref="Passkey"/>, two factors on its own (M3).
    /// </summary>
    public static AuthenticationMethods PasskeyUserVerified { get; } = new(PasskeyUserVerifiedBit);

    /// <summary>Gets the flags as stored, 0 for <see cref="Unknown"/>.</summary>
    public int Value { get; }

    /// <summary>Gets whether nothing was recorded.</summary>
    public bool IsUnknown => Value == 0;

    /// <summary>
    /// Gets whether the session proved who the person is with a first factor: the
    /// password, an external identity, a passkey, or the emailed code of an account
    /// without a password (<see cref="EmailCodeFirstFactor"/>). The emailed code of an
    /// account with a password is not one.
    /// </summary>
    public bool HasPrimary => (Value & PrimaryBits) != 0;

    /// <summary>
    /// Gets whether the session proved the account's password. Only such a sign-in
    /// lifts a lock raised by wrong passwords; a provider, an emailed code or a
    /// challenge recorded as <see cref="Unknown"/> never does.
    /// </summary>
    public bool ProvesPassword => (Value & PasswordBit) != 0;

    /// <summary>
    /// Gets whether the session proved two factors: a first factor and an
    /// authenticator-app code or a recovery code, or a passkey the device verified the
    /// person for (<see cref="PasskeyUserVerified"/>, M3). An emailed code is never the
    /// second of the two, and is the first only as <see cref="EmailCodeFirstFactor"/>; a
    /// passkey without the device's check is a first factor only; <see cref="Unknown"/>
    /// never satisfies it.
    /// </summary>
    public bool IsMfaSatisfied =>
        (HasPrimary && (Value & SecondFactorBits) != 0) || (Value & PasskeyUserVerifiedBit) != 0;

    /// <summary>
    /// What a sign-in whose first proof is the emailed code records: the code, and the
    /// first-factor bit only while the account has no password (D-215-1).
    /// </summary>
    /// <param name="accountHasPassword">Whether the account has a password at this moment.</param>
    public static AuthenticationMethods ForEmailCodeSignIn(bool accountHasPassword) =>
        accountHasPassword ? EmailCode : EmailCode.With(EmailCodeFirstFactor);

    /// <summary>The union of both sets: what a session holds after proving more.</summary>
    public AuthenticationMethods With(AuthenticationMethods other) => new(Value | other.Value);

    /// <summary>
    /// The access token's <c>amr</c> values (RFC 8176): <c>pwd</c>, <c>pop</c> for a
    /// passkey and <c>user</c> when the device verified the person, <c>otp</c>, and
    /// <c>mfa</c> when <see cref="IsMfaSatisfied"/>. Empty for <see cref="Unknown"/>,
    /// and for a set no registered value describes (an external identity alone).
    /// </summary>
    public IReadOnlyList<string> ToAmrValues()
    {
        var values = new List<string>(capacity: 4);

        if ((Value & PasswordBit) != 0)
        {
            values.Add(PasswordAmr);
        }

        if ((Value & PasskeyBit) != 0)
        {
            values.Add(ProofOfPossessionAmr);
        }

        if ((Value & PasskeyUserVerifiedBit) != 0)
        {
            values.Add(UserPresenceAmr);
        }

        if ((Value & TotpBit) != 0)
        {
            values.Add(OneTimePasswordAmr);
        }

        if (IsMfaSatisfied)
        {
            values.Add(MultiFactorAmr);
        }

        return values;
    }

    /// <summary>The stored form: the flags, or <c>NULL</c> for <see cref="Unknown"/>.</summary>
    public int? ToStored() => IsUnknown ? null : Value;

    /// <summary>Reads the stored form. <c>NULL</c> and 0 are <see cref="Unknown"/>.</summary>
    public static AuthenticationMethods FromStored(int? stored) => new(stored ?? 0);

    /// <summary>The method a verified second-factor code proved.</summary>
    public static AuthenticationMethods From(SecondFactorMethod method) =>
        SecondFactors.TryGetValue(method, out var methods)
            ? methods
            : throw new ArgumentOutOfRangeException(nameof(method), method, "A second factor with no authentication method.");

    public override string ToString() => IsUnknown ? "Unknown" : $"AuthenticationMethods {{ Value = {Value} }}";
}
