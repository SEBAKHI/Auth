using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The keyring's transitions (S11, KR3). Each returns a new ring, or the reason the
/// ring cannot make it; nothing here touches the secrets file or the process, so a
/// transition that fails changes nothing anywhere.
/// </summary>
/// <remarks>
/// Every transition that stops a key issuing writes the moment it stopped
/// (<see cref="HmacKey.DemotedAtUtc"/>) and how long it may still verify
/// (<see cref="HmacKey.VerifyUntilUtc"/>): without the first, the overlap rules accept
/// nothing under the old key at all, so a rotation would sign everyone out. When the
/// key that stops issuing is key 1, the same moment is the cutover from the legacy
/// form (KR4), recorded in the same ring, so the first promote of another key records
/// the cutover in the save that makes it current (D-S11B-1).
/// </remarks>
public sealed partial class HmacKeyring
{
    /// <summary>
    /// Stages <paramref name="material"/> as the next key: it verifies from now on and
    /// issues nothing until it is promoted. A ring that already stages a key keeps it
    /// and is returned unchanged (KR3: stage returns the staged key).
    /// </summary>
    public ErrorOr<HmacKeyring> Stage(ReadOnlySpan<byte> material, DateTime nowUtc)
    {
        if (Next is not null)
        {
            return this;
        }

        return WithNewKey(material, nowUtc);
    }

    /// <summary>
    /// Stages key material the administrator supplies. Unlike a generated key, the
    /// material is refused when the ring stages another key already, and when it is
    /// the material of a key the ring holds, key 1 included (S11 SEC-2): a key 2 that
    /// is key 1's material would make key 1 a key-derivation key under another id.
    /// </summary>
    public ErrorOr<HmacKeyring> StageImport(ReadOnlySpan<byte> material, DateTime nowUtc)
    {
        if (Next is { } staged)
        {
            return staged.Material.SequenceEqual(material)
                ? this
                : SecretErrors.InvalidKeyMaterial(
                    $"HMAC key {staged.Id} is already staged. Discard it before staging another key.");
        }

        return WithNewKey(material, nowUtc);
    }

    /// <summary>
    /// Makes the staged key current. The old current key stops issuing now and keeps
    /// verifying until <paramref name="verifyUntilUtc"/> (and no longer than each
    /// purpose's own window).
    /// </summary>
    public ErrorOr<HmacKeyring> Promote(DateTime nowUtc, DateTime verifyUntilUtc)
    {
        if (Next is not { } next)
        {
            return SecretErrors.KeyringNoNextKey;
        }

        var keys = _keys.Values
            .Select(key => key.Id == Current.Id ? Demoted(key, nowUtc, verifyUntilUtc) : key)
            .ToList();

        return Rebuild(keys, next.Id, nextKeyId: null, nowUtc, verifyUntilUtc, closeLegacyNow: false);
    }

    /// <summary>
    /// The answer to a leak: <paramref name="material"/> becomes the current key at
    /// once, and every key the ring held, the staged one included (a leaked file leaks
    /// them all), is marked compromised and verifies nothing from now on, except webhook
    /// keys, which keep their grace (owner decision §11 q2: a leaked HMAC key alone
    /// cannot forge a random webhook key).
    /// </summary>
    public ErrorOr<HmacKeyring> PromoteEmergency(ReadOnlySpan<byte> material, DateTime nowUtc)
    {
        var fresh = AcceptsNewMaterial(material);
        if (fresh.IsError)
        {
            return fresh.Errors;
        }

        var newId = NextFreeId;
        var keys = _keys.Values
            .Select(key => new HmacKey(
                key.Id,
                key.Material,
                key.CreatedAtUtc,
                verifyUntilUtc: nowUtc,
                demotedAtUtc: key.Id == Current.Id ? nowUtc : key.DemotedAtUtc,
                compromised: true))
            .Append(new HmacKey(newId, material, createdAtUtc: nowUtc))
            .ToList();

        return Rebuild(keys, newId, nextKeyId: null, nowUtc, verifyUntilUtc: nowUtc, closeLegacyNow: true);
    }

    /// <summary>Removes the staged key before it is promoted.</summary>
    public ErrorOr<HmacKeyring> DiscardNext()
    {
        if (Next is not { } next)
        {
            return SecretErrors.KeyringNoNextKey;
        }

        return Create(
            _keys.Values.Where(key => key.Id != next.Id),
            Current.Id,
            nextKeyId: null,
            LegacyCutoverUtc,
            LegacyVerifyUntilUtc);
    }

    /// <summary>
    /// Whether <paramref name="keyId"/> is a previous key that can issue again: not
    /// compromised, and still verifying at <paramref name="nowUtc"/>, so nothing it
    /// issued before has been dropped in between.
    /// </summary>
    public bool CanReinstate(int keyId, DateTime nowUtc) =>
        Find(keyId) is { } key &&
        key.Id != Current.Id &&
        key.Id != Next?.Id &&
        !key.Compromised &&
        key.DemotedAtUtc is not null &&
        BoundedVerifyUntil(key) is { } until &&
        nowUtc <= until;

    /// <summary>
    /// Makes a previous key current again: the rollback of a promote (KR6). The
    /// current key becomes a previous key with the window of a promote. Reinstating
    /// key 1 issues the legacy form again, while what its successor issued stays
    /// accepted through that successor's window.
    /// </summary>
    public ErrorOr<HmacKeyring> Reinstate(int keyId, DateTime nowUtc, DateTime verifyUntilUtc)
    {
        if (!CanReinstate(keyId, nowUtc))
        {
            return SecretErrors.KeyringKeyNotFound(keyId);
        }

        var keys = _keys.Values
            .Select(key =>
                key.Id == Current.Id ? Demoted(key, nowUtc, verifyUntilUtc)
                : key.Id == keyId ? new HmacKey(key.Id, key.Material, key.CreatedAtUtc)
                : key)
            .ToList();

        return Rebuild(keys, keyId, Next?.Id, nowUtc, verifyUntilUtc, closeLegacyNow: false);
    }

    /// <summary>Whether <paramref name="keyId"/> is a key a retire may remove: neither current nor staged.</summary>
    public bool CanRetire(int keyId) =>
        Find(keyId) is { } key && key.Id != Current.Id && key.Id != Next?.Id;

    /// <summary>
    /// Removes a previous key for good. Whether live webhook keys still need it is a
    /// question for the database, asked by the caller before this runs.
    /// </summary>
    public ErrorOr<HmacKeyring> Retire(int keyId)
    {
        if (!CanRetire(keyId))
        {
            return SecretErrors.KeyringKeyNotFound(keyId);
        }

        return Create(
            _keys.Values.Where(key => key.Id != keyId),
            Current.Id,
            Next?.Id,
            LegacyCutoverUtc,
            LegacyVerifyUntilUtc);
    }

    /// <summary>
    /// The latest instant <paramref name="key"/> verifies a credential of any bounded
    /// purpose (everything but webhook keys); <see cref="DateTime.MaxValue"/> for the
    /// issuing key, null when it verifies none.
    /// </summary>
    public DateTime? BoundedVerifyUntil(HmacKey key) =>
        CredentialPurposePolicy.Purposes
            .Where(purpose => purpose != CredentialPurpose.WebhookKey)
            .Select(purpose => AcceptedUntil(key, purpose))
            .Max();

    /// <summary>The id the next new key takes: one above every id the ring holds.</summary>
    private int NextFreeId => _keys.Keys.Max() + 1;

    /// <summary>
    /// Whether <paramref name="material"/> may join the ring as a new key: refused when
    /// the ring already holds it under any id. The same material under two ids would
    /// leave a retired key verifying under the other, and a re-import of the current key
    /// as an emergency would mark the key it keeps compromised.
    /// </summary>
    public ErrorOr<Success> AcceptsNewMaterial(ReadOnlySpan<byte> material) =>
        ContainsMaterial(material) is { } existing
            ? SecretErrors.InvalidKeyMaterial($"This key is already HMAC key {existing} of the keyring.")
            : Result.Success;

    private int? ContainsMaterial(ReadOnlySpan<byte> material)
    {
        foreach (var key in _keys.Values)
        {
            if (key.Material.SequenceEqual(material))
            {
                return key.Id;
            }
        }

        return null;
    }

    private ErrorOr<HmacKeyring> WithNewKey(ReadOnlySpan<byte> material, DateTime nowUtc)
    {
        var fresh = AcceptsNewMaterial(material);
        if (fresh.IsError)
        {
            return fresh.Errors;
        }

        var newId = NextFreeId;
        return Create(
            _keys.Values.Append(new HmacKey(newId, material, createdAtUtc: nowUtc)),
            Current.Id,
            newId,
            LegacyCutoverUtc,
            LegacyVerifyUntilUtc);
    }

    // The ring after the current key changed to newCurrentId. When the key that stopped
    // issuing is key 1, that moment is the cutover from the legacy form, recorded here
    // with how long the legacy form may still verify (the old key's own verifyUntilUtc).
    // Every later change keeps that window as it was recorded, except an emergency,
    // which closes it now.
    private ErrorOr<HmacKeyring> Rebuild(
        IReadOnlyCollection<HmacKey> keys,
        int newCurrentId,
        int? nextKeyId,
        DateTime nowUtc,
        DateTime verifyUntilUtc,
        bool closeLegacyNow)
    {
        var legacyStopsIssuing = Current.Id == LegacyKeyId && newCurrentId != LegacyKeyId;

        var legacyVerifyUntil = legacyStopsIssuing
            ? verifyUntilUtc
            : closeLegacyNow && LegacyCutoverUtc is not null ? nowUtc : LegacyVerifyUntilUtc;

        return Create(
            keys,
            newCurrentId,
            nextKeyId,
            legacyStopsIssuing ? nowUtc : LegacyCutoverUtc,
            legacyVerifyUntil);
    }

    private static HmacKey Demoted(HmacKey key, DateTime nowUtc, DateTime verifyUntilUtc) =>
        new(key.Id, key.Material, key.CreatedAtUtc, verifyUntilUtc, demotedAtUtc: nowUtc, key.Compromised);
}
