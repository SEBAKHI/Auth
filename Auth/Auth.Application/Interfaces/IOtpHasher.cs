namespace Auth.Application.Interfaces;

/// <summary>
/// Stores and checks short numeric confirmation codes.
/// </summary>
/// <remarks>
/// Separate from <see cref="IPasswordHasher"/> because the two protect different
/// things and the right tool differs.
/// <para>
/// A password is long-lived, chosen by a person, and very often reused on other
/// sites, so a stolen database must not yield it even to an attacker willing to
/// spend weeks. Deliberate slowness is exactly right there, and nothing here
/// changes it.
/// </para>
/// <para>
/// A confirmation code is six digits, lives five minutes, and dies after five
/// wrong guesses. Slowness buys nothing: a million candidates at the password
/// cost is roughly fifteen hours, and the code has been dead for over fourteen of
/// them. What actually protects it is a key that is not in the database — an
/// attacker holding a stolen table cannot even begin, because they cannot compute
/// a candidate's hash at all.
/// </para>
/// <para>
/// The practical gain is throughput on the path that matters most. Registration
/// paid two password-grade hashes on one request, one of them for the code, and
/// that pair is what caps how many accounts a server can create per second. Only
/// one of them was ever protecting a password.
/// </para>
/// <para>
/// Honest limit: against an attacker who steals the database AND the server's key
/// material, six digits fall instantly. So do most things at that point, and the
/// codes in question expire in minutes. The improvement is against the far more
/// common case of a database copy alone.
/// </para>
/// </remarks>
public interface IOtpHasher
{
    /// <summary>
    /// Returns the stored form of <paramref name="code"/>.
    /// </summary>
    /// <param name="scope">
    /// A stable identifier for whoever the code belongs to — a user id, or an
    /// organization id where the code belongs to the organization rather than a
    /// person. It binds the stored value to that subject, so two rows carrying
    /// the same six digits do not look alike, and a table of precomputed codes
    /// cannot be reused from one row to the next. The SAME value must be passed
    /// to <see cref="Verify"/>, or a correct code is rejected.
    /// </param>
    /// <param name="code">The plaintext code.</param>
    string Hash(string scope, string code);

    /// <summary>
    /// Checks <paramref name="code"/> against a stored value.
    /// </summary>
    /// <remarks>
    /// Accepts both forms. A stored value written by the password hasher before
    /// this existed is verified through it, so codes already in flight at the
    /// moment of deployment keep working. Once those have expired — minutes — the
    /// old form stops appearing on its own; nothing has to migrate it, because
    /// nothing outlives its own expiry.
    /// </remarks>
    bool Verify(string scope, string code, string storedHash);
}
