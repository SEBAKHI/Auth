using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The passkeys of the accounts (S20, PK17): an independent entity with its own
/// repository, like the external logins — the <see cref="User"/> aggregate owns no
/// collections.
/// </summary>
/// <remarks>
/// <para>
/// "Live" is <c>RevokedAt IS NULL</c>, and the reads a person or a policy makes name the
/// current RP ID: a passkey bound to another relying party is neither listed, counted nor
/// accepted (PK3).
/// </para>
/// <para>
/// Every member answers as if the table were empty while the database has no
/// <c>UserPasskeys</c> table (code deployed before its DACPAC), and writes nothing.
/// </para>
/// </remarks>
public interface IUserPasskeyRepository
{
    /// <summary>The account's live passkeys of <paramref name="rpId"/>, oldest first.</summary>
    Task<IReadOnlyList<UserPasskey>> GetLiveAsync(Guid userId, string rpId, CancellationToken cancellationToken);

    /// <summary>The account's live passkey with this id, of <paramref name="rpId"/>, or null.</summary>
    Task<UserPasskey?> GetLiveByIdAsync(Guid userId, Guid passkeyId, string rpId, CancellationToken cancellationToken);

    /// <summary>How many live passkeys of <paramref name="rpId"/> the account holds (FA12).</summary>
    Task<int> CountLiveAsync(Guid userId, string rpId, CancellationToken cancellationToken);

    /// <summary>
    /// The live passkey of <paramref name="rpId"/> whose credential id is exactly
    /// <paramref name="credentialId"/>, of any account (S20 PR B, PK8): the id is compared as
    /// bytes, through the unique index, so two ids that differ only in letter case are two
    /// passkeys (P7-R15).
    /// </summary>
    /// <returns>The passkey, or null when no live one of the RP ID has the id.</returns>
    Task<UserPasskey?> GetLiveByCredentialIdAsync(byte[] credentialId, string rpId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the account with this address holds a live passkey of <paramref name="rpId"/>
    /// (OI-280, D-S20-10): the sign-in page's one question after the address. True only for an
    /// account with a live passkey of the RP ID in force that is not deleted, or that is waiting
    /// for its deletion (its passkey still signs it in, to the recovery); any other deleted
    /// account answers as an unknown address. Whatever else the account is (locked, deactivated,
    /// unconfirmed) is the sign-in's to judge, not this answer's.
    /// </summary>
    /// <remarks>
    /// One statement for every address, known or not, so an unknown address costs the same
    /// one round trip as a known one. Nothing is written.
    /// </remarks>
    /// <param name="email">The address as typed: compared as <c>Users.NormalizedEmail</c> stores it.</param>
    /// <param name="rpId">The RP ID in force.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasLiveForEmailAsync(string email, string rpId, CancellationToken cancellationToken);

    /// <summary>
    /// The account's WebAuthn user handle, read whatever the account's state — a soft-deleted
    /// account included — and nothing else of it (PK8, FA6): a sign-in compares it with the
    /// handle the authenticator answered before anything about the owner is read.
    /// </summary>
    /// <returns>The handle; null when the account has none or does not exist.</returns>
    Task<byte[]?> GetUserHandleAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a verified sign-in on the passkey (PK9, T-B4), in one statement: the new
    /// counter, the BE and BS flags and the time, only while the passkey is live and its
    /// counter is still <paramref name="storedSignCount"/>, the value the sign-in was checked
    /// against. A removal, a reset or another sign-in that got there first matches nothing.
    /// </summary>
    /// <param name="passkey">The passkey as <see cref="UserPasskey.RecordAssertion"/> left it.</param>
    /// <param name="storedSignCount">The counter read before the check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the row was written; false when it matched nothing.</returns>
    Task<bool> TryRecordAssertionAsync(UserPasskey passkey, long storedSignCount, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a sign-in's counter did not grow (PK9): <c>LastCounterRegressionAt</c>,
    /// and nothing else. The passkey is never removed or revoked by it.
    /// </summary>
    Task SealCounterRegressionAsync(Guid passkeyId, DateTime atUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any account holds a live passkey bound to an RP ID other than
    /// <paramref name="rpId"/> (PK3): such passkeys sign in nowhere and count as no factor.
    /// </summary>
    Task<bool> HasLiveOfAnotherRpIdAsync(string rpId, CancellationToken cancellationToken);

    /// <summary>
    /// The account's WebAuthn user handle (D-T20 A): set once, by the first registration, to
    /// <paramref name="candidate"/> by a conditional statement, and read back. Two first
    /// registrations at once both read the one that won.
    /// </summary>
    /// <returns>The account's handle; null when the account does not exist.</returns>
    Task<byte[]?> EnsureUserHandleAsync(Guid userId, byte[] candidate, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new passkey, only while the account holds fewer than
    /// <paramref name="maxLive"/> live ones of its RP ID and while the session that
    /// authorised it is still live, in one transaction with spending the emailed
    /// first-factor code the bind was checked with, when it needed one (X02).
    /// </summary>
    /// <remarks>
    /// The session is read under a lock its end must wait for (REVIEW 150 F1): every path
    /// that takes the account back ends its sessions before it revokes the recent passkeys,
    /// so a registration in flight either commits first, and the revocation catches it, or
    /// meets an ended session, and stores nothing. With <paramref name="requireAnotherFactor"/>
    /// the account must still hold another factor when the row is written — the authenticator
    /// app or another live passkey — read under a lock the app's switch-off waits for, so a
    /// registration and a switch-off at the same moment never leave a passkey as an account's
    /// only factor while email is off (REVIEW-FIXES 150 F19).
    /// </remarks>
    /// <param name="passkey">The passkey.</param>
    /// <param name="maxLive">The most live passkeys the account may hold.</param>
    /// <param name="bindCodeId">The emailed code the bind was checked with, or null.</param>
    /// <param name="sessionId">The session the registration's checks accepted.</param>
    /// <param name="requireAnotherFactor">Whether email is off, so another factor must remain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="PasskeyInsertOutcome.Inserted"/>; <see cref="PasskeyInsertOutcome.LimitReached"/>;
    /// <see cref="PasskeyInsertOutcome.DuplicateCredential"/> when the credential id is
    /// registered already, to any account; <see cref="PasskeyInsertOutcome.BindCodeLost"/>
    /// when the code was spent or superseded by another request first; or
    /// <see cref="PasskeyInsertOutcome.SessionEnded"/> when the session ended meanwhile; or
    /// <see cref="PasskeyInsertOutcome.AnotherFactorRequired"/> when the other factor went.
    /// Nothing is stored, and the code is not spent, unless it is
    /// <see cref="PasskeyInsertOutcome.Inserted"/>.
    /// </returns>
    Task<PasskeyInsertOutcome> TryCreateAsync(
        UserPasskey passkey,
        int maxLive,
        Guid? bindCodeId,
        Guid sessionId,
        bool requireAnotherFactor,
        CancellationToken cancellationToken);

    /// <summary>Renames the account's live passkey; false when it matched nothing.</summary>
    Task<bool> TryRenameAsync(Guid userId, Guid passkeyId, string rpId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the account's live passkey of <paramref name="rpId"/> in one statement: only a
    /// live row of the caller matches.
    /// </summary>
    /// <returns>The passkey as it was revoked, or null when nothing matched.</returns>
    Task<UserPasskey?> TryRevokeAsync(
        Guid userId,
        Guid passkeyId,
        string rpId,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the account's live passkey of <paramref name="rpId"/> as
    /// <see cref="TryRevokeAsync"/> does, only while the account holds another live one of
    /// it: the count is read under a lock held to the end of the statement, so of two
    /// removals that would each take one of the last two, one is refused (REVIEW 150 F4).
    /// </summary>
    /// <returns>
    /// The passkey as it was revoked, or null when nothing matched: not the caller's live
    /// passkey, or the last one, which stays.
    /// </returns>
    Task<UserPasskey?> TryRevokeUnlessLastAsync(
        Guid userId,
        Guid passkeyId,
        string rpId,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every live passkey of the account registered after <paramref name="registeredAfterUtc"/>,
    /// of any RP ID, in one statement (D-T19 D).
    /// </summary>
    /// <returns>The passkeys revoked.</returns>
    Task<IReadOnlyList<UserPasskey>> RevokeRegisteredAfterAsync(
        Guid userId,
        DateTime registeredAfterUtc,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken);
}

/// <summary>The outcome of storing a new passkey.</summary>
public enum PasskeyInsertOutcome
{
    /// <summary>Stored.</summary>
    Inserted = 0,

    /// <summary>The account already holds the most live passkeys it may.</summary>
    LimitReached = 1,

    /// <summary>The credential id is registered already, to some account.</summary>
    DuplicateCredential = 2,

    /// <summary>The emailed code the bind was checked with was spent or superseded first.</summary>
    BindCodeLost = 3,

    /// <summary>The session the registration's checks accepted ended before the row was written.</summary>
    SessionEnded = 4,

    /// <summary>
    /// Email is off and the account no longer held another factor when the row was to be
    /// written: the app was switched off meanwhile (REVIEW-FIXES 150 F19).
    /// </summary>
    AnotherFactorRequired = 5
}
