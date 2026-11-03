using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Builds what the administrator sees of the RSA signing keyring (S12) from the ring the
/// secrets file holds and the ring this process signs under. Names, states and dates only.
/// </summary>
public static class SigningKeyringView
{
    /// <summary>The view of a ring the file holds.</summary>
    /// <param name="file">The ring the secrets file holds.</param>
    /// <param name="process">The ring this process signs and verifies under.</param>
    /// <param name="source">Where this process took its signing key from.</param>
    /// <param name="nowUtc">The moment the view describes.</param>
    /// <param name="changeAppliedInProcess">On a change's answer, whether this process applied it; null on a read.</param>
    public static SigningKeyringDto Build(
        RsaKeyring file,
        RsaKeyring process,
        SigningKeySource source,
        DateTime nowUtc,
        bool? changeAppliedInProcess = null) => new()
        {
            Available = true,
            KeySource = source.Setting,
            CurrentKid = file.Current.Kid,
            ProcessCurrentKid = process.Current.Kid,
            NextKid = file.Next?.Kid,
            ProcessNextKid = process.Next?.Kid,
            Persisted = file.Persisted,
            PromoteAvailableAtUtc = file.PromoteAvailableAtUtc,
            InSync = SameRing(file, process),
            ChangeAppliedInProcess = changeAppliedInProcess,
            Keys = file.Keys
                .OrderBy(key => Order(file, key))
                .ThenByDescending(key => key.VerifyUntilUtc)
                .Select(key => Entry(file, key, process, nowUtc, inFile: true))
                .Concat(process.Keys
                    .Where(key => file.Find(key.Kid) is null)
                    .OrderBy(key => Order(process, key))
                    .Select(key => Entry(process, key, process, nowUtc, inFile: false)))
                .ToList()
        };

    /// <summary>
    /// The view when there is no ring to manage, or the file's cannot be read: what this process
    /// signs with, and why nothing can be changed here.
    /// </summary>
    public static SigningKeyringDto Unavailable(
        RsaKeyring process,
        SigningKeySource source,
        DateTime nowUtc,
        string reason,
        string? errorCode = null) => new()
        {
            Available = false,
            UnavailableReason = reason,
            KeySource = source.Setting,
            ErrorCode = errorCode,
            ProcessCurrentKid = process.Current.Kid,
            ProcessNextKid = process.Next?.Kid,
            InSync = true,
            Keys = process.Keys
                .OrderBy(key => Order(process, key))
                .Select(key => Entry(process, key, process, nowUtc, inFile: false))
                .ToList()
        };

    /// <summary>
    /// Whether the file and this process hold the same ring: the same current and staged key,
    /// and every key of either in the other, in the same state. What the in-sync flag and the
    /// posture compare.
    /// </summary>
    public static bool SameRing(RsaKeyring file, RsaKeyring process) =>
        RsaKeyringFile.SameKey(file.Current, process.Current)
        && RsaKeyringFile.SameKey(file.Next, process.Next)
        && file.Keys.Count == process.Keys.Count
        && file.Keys.All(key => process.Find(key.Kid) is { } held
            && RsaKeyringFile.SameKey(key, held)
            && key.Compromised == held.Compromised);

    private static SigningKeyringEntryDto Entry(RsaKeyring ring, SigningKey key, RsaKeyring process, DateTime nowUtc, bool inFile)
    {
        var held = process.Find(key.Kid) is { } loaded && RsaKeyringFile.SameKey(loaded, key) ? loaded : null;
        return new SigningKeyringEntryDto
        {
            Kid = key.Kid,
            State = StateOf(ring, key),
            Migrated = key.IsMigrated,
            CreatedAtUtc = key.CreatedAtUtc,
            PublishedAtUtc = key.PublishedAtUtc,
            VerifyUntilUtc = ring.IsCurrent(key) || ring.IsNext(key) ? null : key.VerifyUntilUtc,
            Compromised = key.Compromised,
            InFile = inFile,
            LoadedInProcess = held is not null,
            AcceptedInProcess = held is not null && process.Verifies(held, nowUtc)
        };
    }

    private static SigningKeyState StateOf(RsaKeyring ring, SigningKey key) =>
        key.Compromised ? SigningKeyState.Compromised
        : ring.IsCurrent(key) ? SigningKeyState.Current
        : ring.IsNext(key) ? SigningKeyState.Next
        : SigningKeyState.Previous;

    private static int Order(RsaKeyring ring, SigningKey key) => (int)StateOf(ring, key);
}
