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
/// Password = 1, ExternalIdentity = 2, EmailCode = 4, Totp = 8, RecoveryCode = 16.
/// 32 and 64 are reserved for the passkey methods. <see cref="Unknown"/> (no bit)
/// is stored as <c>NULL</c>: a session recorded before the column existed, or one
/// whose row could not be written, proved nothing anyone can read back.
/// </para>
/// <para>
/// An emailed code is recorded but is never a factor of either kind: it proves a
/// mailbox, which the password-reset path already hands to whoever holds it.
/// </para>
/// </remarks>
public readonly record struct AuthenticationMethods
{
    private const int PasswordBit = 1;
    private const int ExternalIdentityBit = 2;
    private const int EmailCodeBit = 4;
    private const int TotpBit = 8;
    private const int RecoveryCodeBit = 16;

    // Bits 32 and 64 belong to the passkey methods and are not issued yet. A
    // stored value carrying any other bit was not written by this code: it is
    // read as what it holds of the known bits, never trusted beyond them.
    private const int KnownBits = PasswordBit | ExternalIdentityBit | EmailCodeBit | TotpBit | RecoveryCodeBit;

    private const int PrimaryBits = PasswordBit | ExternalIdentityBit;
    private const int SecondFactorBits = TotpBit | RecoveryCodeBit;

    // RFC 8176 values. Only the password and the authenticator app have one;
    // an external identity, an emailed code and a recovery code are reflected by
    // "mfa" alone. An emailed code is never "otp": that value means HOTP/TOTP.
    private const string PasswordAmr = "pwd";
    private const string OneTimePasswordAmr = "otp";
    private const string MultiFactorAmr = "mfa";

    private static readonly FrozenDictionary<SecondFactorMethod, AuthenticationMethods> SecondFactors =
        new Dictionary<SecondFactorMethod, AuthenticationMethods>
        {
            [SecondFactorMethod.Totp] = new(TotpBit),
            [SecondFactorMethod.RecoveryCode] = new(RecoveryCodeBit),
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

    /// <summary>A code sent to the account's email address. Never a factor.</summary>
    public static AuthenticationMethods EmailCode { get; } = new(EmailCodeBit);

    /// <summary>A code from the account's authenticator app.</summary>
    public static AuthenticationMethods Totp { get; } = new(TotpBit);

    /// <summary>One of the account's single-use recovery codes.</summary>
    public static AuthenticationMethods RecoveryCode { get; } = new(RecoveryCodeBit);

    /// <summary>Gets the flags as stored, 0 for <see cref="Unknown"/>.</summary>
    public int Value { get; }

    /// <summary>Gets whether nothing was recorded.</summary>
    public bool IsUnknown => Value == 0;

    /// <summary>
    /// Gets whether the session proved who the person is with a first factor: the
    /// password or an external identity. An emailed code is not one.
    /// </summary>
    public bool HasPrimary => (Value & PrimaryBits) != 0;

    /// <summary>
    /// Gets whether the session proved two factors: a first factor and an
    /// authenticator-app code or a recovery code. An emailed code counts as
    /// neither, and <see cref="Unknown"/> never satisfies it.
    /// </summary>
    public bool IsMfaSatisfied => HasPrimary && (Value & SecondFactorBits) != 0;

    /// <summary>The union of both sets: what a session holds after proving more.</summary>
    public AuthenticationMethods With(AuthenticationMethods other) => new(Value | other.Value);

    /// <summary>
    /// The access token's <c>amr</c> values (RFC 8176): <c>pwd</c>, <c>otp</c>, and
    /// <c>mfa</c> when <see cref="IsMfaSatisfied"/>. Empty for <see cref="Unknown"/>,
    /// and for a set no registered value describes (an external identity alone).
    /// </summary>
    public IReadOnlyList<string> ToAmrValues()
    {
        var values = new List<string>(capacity: 3);

        if ((Value & PasswordBit) != 0)
        {
            values.Add(PasswordAmr);
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
