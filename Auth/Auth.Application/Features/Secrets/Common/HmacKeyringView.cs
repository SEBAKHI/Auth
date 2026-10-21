using Auth.Application.DTOs;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Builds what the administrator sees of the HMAC keyring from the ring the secrets
/// file holds and the ring this process holds. Ids, states and dates only: nothing it
/// returns is, or is derived from, key material.
/// </summary>
public static class HmacKeyringView
{
    /// <summary>The keyring view.</summary>
    /// <param name="file">The ring the secrets file holds.</param>
    /// <param name="process">The ring this process issues and verifies under.</param>
    /// <param name="webhookKeysByKeyId">Live webhook keys by the HMAC key their fingerprint was computed under.</param>
    public static HmacKeyringDto Build(
        HmacKeyring file,
        HmacKeyring process,
        IReadOnlyDictionary<int, int> webhookKeysByKeyId) => new()
        {
            CurrentKeyId = file.Current.Id,
            ProcessCurrentKeyId = process.Current.Id,
            NextKeyId = file.Next?.Id,
            ProcessNextKeyId = process.Next?.Id,
            InSync = HmacKeyringFile.SameIssuingState(file, process),
            LegacyCutoverUtc = file.LegacyCutoverUtc,
            Keys = file.Keys
                .OrderBy(key => key.Id)
                .Select(key => Entry(file, process, key, webhookKeysByKeyId))
                .ToList()
        };

    private static HmacKeyringEntryDto Entry(
        HmacKeyring file,
        HmacKeyring process,
        HmacKey key,
        IReadOnlyDictionary<int, int> webhookKeysByKeyId)
    {
        var isCurrent = key.Id == file.Current.Id;
        return new HmacKeyringEntryDto
        {
            Id = key.Id,
            State = isCurrent ? HmacKeyState.Current
                : key.Id == file.Next?.Id ? HmacKeyState.Next
                : HmacKeyState.Previous,
            CreatedAtUtc = key.CreatedAtUtc,
            DemotedAtUtc = key.DemotedAtUtc,
            VerifyUntilUtc = isCurrent ? null : file.BoundedVerifyUntil(key),
            WebhookGraceUntilUtc = isCurrent ? null : file.AcceptedUntil(key, CredentialPurpose.WebhookKey),
            Compromised = key.Compromised,
            WebhookKeyCount = webhookKeysByKeyId.GetValueOrDefault(key.Id),
            LoadedInProcess = HmacKeyringFile.SameKey(process.Find(key.Id), key)
        };
    }
}
