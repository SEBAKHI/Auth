using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The signing keyring's transitions (S12, KR3). Each returns a new ring, or the reason the
/// ring cannot make it; nothing here touches the secrets file or a process, so a transition
/// that fails changes nothing anywhere. The same transition is applied to the ring the file
/// holds and, when this process loaded the keys it needs, to the ring the process signs under.
/// </summary>
/// <remarks>
/// A key that stops signing keeps only its public key (KR1): a previous key and a compromised
/// record carry no private key, so once the file is saved twice (promote, retire) neither the
/// file nor its backup holds the demoted private key.
/// </remarks>
public sealed partial class RsaKeyring
{
    /// <summary>
    /// Stages <paramref name="generated"/>: it is accepted and published from the next process
    /// start, and signs nothing until it is promoted. A ring that already stages a key keeps it
    /// and is returned unchanged (KR3: stage returns the staged key).
    /// </summary>
    public ErrorOr<RsaKeyring> Stage(SigningKey generated, DateTime nowUtc) =>
        Next is not null ? this : WithStaged(generated, nowUtc);

    /// <summary>
    /// Stages a key the administrator supplies. Refused while another key is staged, and for a
    /// key the ring holds in any state or once held: a key that left the ring may have leaked,
    /// and taken back it would sign again with nothing to say so. The staged key itself is
    /// returned unchanged.
    /// </summary>
    public ErrorOr<RsaKeyring> StageImport(SigningKey supplied, DateTime nowUtc)
    {
        if (Next is { } staged)
        {
            return string.Equals(staged.Thumbprint, supplied.Thumbprint, StringComparison.Ordinal)
                ? this
                : SecretErrors.KeyringKeyReferenced(staged.Kid);
        }

        return WithStaged(supplied, nowUtc);
    }

    /// <summary>
    /// Whether a key with <paramref name="thumbprint"/> may enter the ring: not one it holds, in
    /// any state, and not one it remembers. Checked before a confirmation is spent, and again by
    /// the transition.
    /// </summary>
    public ErrorOr<Success> AcceptsNewKey(string thumbprint)
    {
        if (FindByThumbprint(thumbprint) is { } held)
        {
            return SecretErrors.KeyringKeyReferenced(held.Kid);
        }

        // A kid is the thumbprint, and kids compare without case: a key whose name would collide
        // with a held one is refused as that one.
        if (Find(thumbprint) is { } sameName)
        {
            return SecretErrors.KeyringKeyReferenced(sameName.Kid);
        }

        return Removed.Any(key => string.Equals(key.Thumbprint, thumbprint, StringComparison.Ordinal))
            ? SecretErrors.KeyringKeyReferenced(thumbprint)
            : Result.Success;
    }

    /// <summary>
    /// Makes the staged key current. The old current key stops signing now, loses its private
    /// key, and is accepted until <paramref name="verifyUntilUtc"/>
    /// (<see cref="SigningKeyOverlap.PreviousVerifyUntil"/>).
    /// </summary>
    public ErrorOr<RsaKeyring> Promote(DateTime nowUtc, DateTime verifyUntilUtc)
    {
        if (Next is not { } next)
        {
            return SecretErrors.KeyringNoNextKey;
        }

        return Rebuild(
            _keys.Values.Select(key => IsCurrent(key) ? Demoted(key, verifyUntilUtc, compromised: false) : key),
            next.Kid,
            nextKid: null,
            Removed);
    }

    /// <summary>
    /// The answer to a leak of the current key: the staged key signs at once, and the old current
    /// key becomes a compromised record (its public key only), neither accepted nor published,
    /// and never staged again.
    /// </summary>
    public ErrorOr<RsaKeyring> PromoteEmergency(DateTime nowUtc)
    {
        if (Next is not { } next)
        {
            return SecretErrors.KeyringNoNextKey;
        }

        return Rebuild(
            _keys.Values.Select(key => IsCurrent(key) ? Demoted(key, nowUtc, compromised: true) : key),
            next.Kid,
            nextKid: null,
            Removed);
    }

    /// <summary>
    /// What the generate/rsa and import/rsa aliases do (PB3): stage <paramref name="replacement"/>
    /// and promote it as an emergency, in one change. A key already staged is discarded first
    /// (the alias always installs the key it was given), unless it is that key.
    /// </summary>
    public ErrorOr<RsaKeyring> ReplaceInEmergency(SigningKey replacement, DateTime nowUtc)
    {
        if (Next is { } staged)
        {
            if (string.Equals(staged.Thumbprint, replacement.Thumbprint, StringComparison.Ordinal))
            {
                return PromoteEmergency(nowUtc);
            }

            var discarded = DiscardNext(nowUtc);
            return discarded.IsError ? discarded.Errors : discarded.Value.ReplaceInEmergency(replacement, nowUtc);
        }

        var withStaged = WithStaged(replacement, nowUtc);
        return withStaged.IsError ? withStaged.Errors : withStaged.Value.PromoteEmergency(nowUtc);
    }

    /// <summary>Removes the staged key before it ever signed; the ring remembers it as discarded.</summary>
    public ErrorOr<RsaKeyring> DiscardNext(DateTime nowUtc)
    {
        if (Next is not { } next)
        {
            return SecretErrors.KeyringNoNextKey;
        }

        return Rebuild(
            _keys.Values.Where(key => !IsNext(key)),
            Current.Kid,
            nextKid: null,
            [.. Removed, RemovedSigningKey.Of(next, nowUtc, SigningKeyRemoval.Discarded)]);
    }

    /// <summary>
    /// Whether <paramref name="kid"/> names a key a retire applies to: a previous key, or a
    /// compromised record. The current and the staged key are refused as in use.
    /// </summary>
    public ErrorOr<SigningKey> CanRetire(string kid)
    {
        if (Find(kid) is not { } key)
        {
            return SecretErrors.KeyringKeyNotFound(kid);
        }

        return IsCurrent(key) || IsNext(key)
            ? SecretErrors.KeyringKeyReferenced(key.Kid)
            : key;
    }

    /// <summary>
    /// Removes a previous key or a compromised record for good; the ring remembers it as
    /// retired. A previous key still in its window stops being accepted now.
    /// </summary>
    public ErrorOr<RsaKeyring> Retire(string kid, DateTime nowUtc)
    {
        var retiring = CanRetire(kid);
        if (retiring.IsError)
        {
            return retiring.Errors;
        }

        var key = retiring.Value;
        return Rebuild(
            _keys.Values.Where(held => !string.Equals(held.Kid, key.Kid, StringComparison.OrdinalIgnoreCase)),
            Current.Kid,
            Next?.Kid,
            [.. Removed, RemovedSigningKey.Of(key, nowUtc, SigningKeyRemoval.Retired)]);
    }

    private ErrorOr<RsaKeyring> WithStaged(SigningKey key, DateTime nowUtc)
    {
        var accepted = AcceptsNewKey(key.Thumbprint);
        if (accepted.IsError)
        {
            return accepted.Errors;
        }

        // Every new key is named by its thumbprint (KR2) and dated now; it is published from the
        // first process start that loads it.
        var staged = key with
        {
            Kid = key.Thumbprint,
            CreatedAtUtc = nowUtc,
            PublishedAtUtc = null,
            VerifyUntilUtc = null,
            Compromised = false
        };

        return Rebuild(_keys.Values.Append(staged), Current.Kid, staged.Kid, Removed);
    }

    // A key that stops signing keeps its public key only.
    private static SigningKey Demoted(SigningKey key, DateTime verifyUntilUtc, bool compromised) =>
        key with { PrivateKeyPem = null, VerifyUntilUtc = verifyUntilUtc, Compromised = compromised };

    // Every transition's result is saved, so it is persisted.
    private static ErrorOr<RsaKeyring> Rebuild(
        IEnumerable<SigningKey> keys,
        string currentKid,
        string? nextKid,
        IReadOnlyList<RemovedSigningKey> removed) =>
        Create(keys.ToList(), currentKid, nextKid, removed, persisted: true);
}
