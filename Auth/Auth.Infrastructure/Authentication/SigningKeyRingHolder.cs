using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using ErrorOr;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// The process's signing keys (S12): the one <see cref="ISigningKeyMaterial"/> the signer, every
/// validator, the JWKS and the readiness probe read, and the one <see cref="ISigningKeyRing"/> the
/// keyring commands change. Loaded once at start by the "key material validation" step; a keyring
/// change that saved the secrets file swaps its result in here, whole.
/// </summary>
/// <remarks>
/// Each member reads the snapshot in force once, so a token is signed, or checked, under one
/// snapshot, never half of two. Like <c>HmacKeyringHolder</c>, it is swapped only after the save.
/// </remarks>
public sealed class SigningKeyRingHolder : ISigningKeyMaterial, ISigningKeyRing
{
    private SigningKeyRing _current;

    public SigningKeyRingHolder(SigningKeyRing initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    /// <summary>The snapshot in force.</summary>
    public SigningKeyRing Snapshot => Volatile.Read(ref _current);

    /// <inheritdoc />
    public SigningCredentials Current => Snapshot.Current;

    /// <inheritdoc />
    public SigningKeySource Source => Snapshot.Source;

    /// <inheritdoc />
    RsaKeyring ISigningKeyRing.Current => Snapshot.Ring;

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now) => Snapshot.VerificationKeys(now);

    /// <inheritdoc />
    public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) => Snapshot.PublishedKeys(now);

    /// <inheritdoc />
    /// <remarks>Call only after the secrets file holds the change: a ring the file does not hold would be lost at the next start.</remarks>
    public bool TryApply(Func<RsaKeyring, ErrorOr<RsaKeyring>> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var before = Snapshot;
        var changed = change(before.Ring);
        if (changed.IsError || before.With(changed.Value) is not { } after)
        {
            return false;
        }

        Volatile.Write(ref _current, after);
        return true;
    }
}
