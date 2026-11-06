using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using ErrorOr;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// What a lock does to a passkey sign-in (S20 PR B, FA6, ARR-1043, owner decision 2026-09-28):
/// an administrator's live lock refuses it with the answer the password sign-in gives; a lock
/// raised by wrong passwords — timed or hard — admits it and lifts nothing.
/// </summary>
/// <remarks>
/// <para>
/// Why the hard lock admits a passkey: anyone who knows the address can push an account to the
/// hard lock with wrong passwords in a few hours, and a passkey is not a password — refusing it
/// would let a stranger lock its owner out of the one way in a look-alike site cannot relay. The
/// password sign-in, the provider sign-in and a password change still refuse under the hard lock.
/// </para>
/// <para>
/// Admitted means only that: the session the sign-in mints does not prove the password
/// (<c>passwordProven: false</c>), so the lock and the failure count stay exactly as they were
/// (owner decision G8). Each admission is a Warning line with the lock's kind
/// (<see cref="PasskeySignalCodes.AdmittedWhileLocked"/>), counted by the owner's review of the
/// exception ([S20-O7]). A lapsed lock refuses nothing, as on every door.
/// </para>
/// </remarks>
public static class PasskeyLockAdmission
{
    /// <summary>The refusal of a passkey sign-in to <paramref name="owner"/>, or null when it is admitted.</summary>
    /// <param name="owner">The account the passkey belongs to.</param>
    /// <param name="nowUtc">Now (UTC).</param>
    /// <param name="logger">Where the admission under a lock is reported.</param>
    public static Error? Judge(User owner, DateTime nowUtc, ILogger logger)
    {
        var kind = owner.LockKind(nowUtc);
        if (kind == AccountLockKind.Admin)
        {
            return owner.LockRefusal(kind);
        }

        if (kind is AccountLockKind.Timed or AccountLockKind.Hard)
        {
            logger.LogWarning(
                "{EventCode}: the passkey sign-in of user {UserId} was admitted under a {LockKind} lock; nothing was lifted",
                PasskeySignalCodes.AdmittedWhileLocked, owner.Id, kind);
        }

        return null;
    }
}
