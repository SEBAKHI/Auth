namespace Auth.Application.Security.Keyring;

/// <summary>
/// The process's current <see cref="HmacKeyring"/>. Loaded once at startup by the
/// "key material validation" step; a keyring transition that saved the secrets
/// file swaps its result in here, whole, so the process issues and verifies under
/// what it just saved without a restart (KR3).
/// </summary>
/// <remarks>
/// Readers take <see cref="Current"/> once per operation and use that one ring
/// for the whole operation, so a lookup's candidates and a fingerprint issued in
/// the same request never come from two different rings.
/// </remarks>
public sealed class HmacKeyringHolder
{
    private HmacKeyring _current;

    public HmacKeyringHolder(HmacKeyring initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    /// <summary>The ring in force.</summary>
    public HmacKeyring Current => Volatile.Read(ref _current);

    /// <summary>
    /// Replaces the ring. Call only after the ring was saved to the secrets file:
    /// a ring the file does not hold would be lost at the next restart.
    /// </summary>
    public void Replace(HmacKeyring ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        Volatile.Write(ref _current, ring);
    }
}
