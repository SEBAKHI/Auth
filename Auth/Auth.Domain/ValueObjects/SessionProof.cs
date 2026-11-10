namespace Auth.Domain.ValueObjects;

/// <summary>
/// The last time a signed-in session proved itself again, with no new sign-in, and what that
/// one proof proved.
/// </summary>
/// <remarks>
/// <para>
/// What it proved, never what the session ever proved (<c>UserSessions.AuthMethods</c>): a session
/// that proved two factors an hour ago and a password just now has proved a password just now. A
/// change that needs a recent two-factor proof reads this, so a stolen session and a stolen
/// password together never pass for two factors.
/// </para>
/// <para>
/// No invariant can fail on its inputs, so the constructor is public. Its time is the database's
/// (<c>UserSessions.LastProofAt</c>), in UTC.
/// </para>
/// </remarks>
public sealed record class SessionProof
{
    /// <param name="provedAtUtc">When the proof was accepted (UTC).</param>
    /// <param name="methods">What that one proof proved.</param>
    public SessionProof(DateTime provedAtUtc, AuthenticationMethods methods)
    {
        ProvedAtUtc = provedAtUtc;
        Methods = methods;
    }

    /// <summary>Gets when the proof was accepted (UTC).</summary>
    public DateTime ProvedAtUtc { get; }

    /// <summary>Gets what that one proof proved.</summary>
    public AuthenticationMethods Methods { get; }
}
