using System.Buffers.Text;
using Auth.Domain.Entities;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// One passkey as its owner sees it (S20): the name, when it was added and last used, whether
/// it is synced, and its credential id. Never the public key.
/// </summary>
/// <remarks>
/// The credential id is the owner's to read (S20 PR C, D-S20-7): the security page tells the
/// browser which passkeys the account still has, so a password manager can drop one that was
/// removed. It is answered to the signed-in owner only, and never logged (PK16).
/// </remarks>
/// <param name="Id">The passkey's row id: what rename and remove name.</param>
/// <param name="Name">The name its owner gave it.</param>
/// <param name="CreatedAt">When it was added (UTC).</param>
/// <param name="LastUsedAt">When it last signed in (UTC), or null.</param>
/// <param name="IsBackupEligible">Whether it may be synced to the owner's other devices.</param>
/// <param name="IsBackedUp">Whether it was synced when last seen.</param>
/// <param name="LastCounterRegressionAt">When its counter last went backwards, or null.</param>
/// <param name="CredentialId">The WebAuthn credential id, base64url without padding.</param>
public sealed record PasskeyDto(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsBackupEligible,
    bool IsBackedUp,
    DateTime? LastCounterRegressionAt,
    string CredentialId)
{
    /// <summary>The owner's view of a passkey.</summary>
    public static PasskeyDto From(UserPasskey passkey) => new(
        passkey.Id,
        passkey.Name,
        passkey.CreatedAt,
        passkey.LastUsedAt,
        passkey.IsBackupEligible,
        passkey.IsBackedUp,
        passkey.LastCounterRegressionAt,
        Base64Url.EncodeToString(passkey.CredentialId));
}

/// <summary>
/// The signed-in account's passkeys as its security page reads them (S20 PR C): the live
/// passkeys of the RP ID in force, and the two facts the page needs to tell the browser which
/// of them the account still has (D-S20-7, <c>PublicKeyCredential.signalAllAcceptedCredentials</c>).
/// </summary>
/// <remarks>
/// Answered to the signed-in owner only, and never logged (PK16): the user handle names the
/// account to its own authenticators, and the credential ids name its passkeys.
/// </remarks>
/// <param name="RpId">The RP ID the passkeys are bound to: the one this process started with.</param>
/// <param name="UserHandle">
/// The account's WebAuthn user handle, base64url without padding; null while the account has
/// none (it never added a passkey), when there is nothing to tell a browser about.
/// </param>
/// <param name="Passkeys">The live passkeys of <paramref name="RpId"/>, oldest first.</param>
public sealed record PasskeyListDto(
    string RpId,
    string? UserHandle,
    IReadOnlyList<PasskeyDto> Passkeys);
