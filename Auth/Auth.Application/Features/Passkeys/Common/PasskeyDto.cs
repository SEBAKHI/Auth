using Auth.Domain.Entities;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// One passkey as its owner sees it in the list (S20): the name, when it was added and last
/// used, and whether it is synced. Never the credential id, the public key or the RP ID.
/// </summary>
/// <param name="Id">The passkey's row id: what rename and remove name.</param>
/// <param name="Name">The name its owner gave it.</param>
/// <param name="CreatedAt">When it was added (UTC).</param>
/// <param name="LastUsedAt">When it last signed in (UTC), or null.</param>
/// <param name="IsBackupEligible">Whether it may be synced to the owner's other devices.</param>
/// <param name="IsBackedUp">Whether it was synced when last seen.</param>
/// <param name="LastCounterRegressionAt">When its counter last went backwards, or null.</param>
public sealed record PasskeyDto(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsBackupEligible,
    bool IsBackedUp,
    DateTime? LastCounterRegressionAt)
{
    /// <summary>The owner's view of a passkey.</summary>
    public static PasskeyDto From(UserPasskey passkey) => new(
        passkey.Id,
        passkey.Name,
        passkey.CreatedAt,
        passkey.LastUsedAt,
        passkey.IsBackupEligible,
        passkey.IsBackedUp,
        passkey.LastCounterRegressionAt);
}
