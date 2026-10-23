namespace Auth.Domain.Enums;

/// <summary>
/// What a two-factor challenge row is for. One table holds them all — they share
/// the attempt reservation, the expiry, the emailed-code columns and the sweep —
/// and every statement that reads or spends a row names the purpose it serves, so
/// a row opened for one purpose can never answer another.
/// </summary>
/// <remarks>
/// Stored as a <c>TINYINT</c> (<c>TwoFactorChallenges.Purpose</c>). The values are
/// fixed and never renumbered; every row written before the column existed is a
/// sign-in (the column's default, 1).
/// </remarks>
public enum TwoFactorChallengePurpose : byte
{
    /// <summary>The second step of a sign-in, answered with the opaque challenge token.</summary>
    SignIn = 1,

    /// <summary>Switching the emailed code on: the code proves the mailbox before it is bound.</summary>
    EmailEnrollment = 2,

    /// <summary>The recovery of an account waiting for deletion, by its password and an emailed code.</summary>
    DeletionRecovery = 3,

    /// <summary>Switching the emailed code off with a code sent to the mailbox.</summary>
    EmailDisable = 4
}
