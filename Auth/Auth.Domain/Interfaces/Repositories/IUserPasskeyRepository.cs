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
    /// meets an ended session, and stores nothing.
    /// </remarks>
    /// <param name="passkey">The passkey.</param>
    /// <param name="maxLive">The most live passkeys the account may hold.</param>
    /// <param name="bindCodeId">The emailed code the bind was checked with, or null.</param>
    /// <param name="sessionId">The session the registration's checks accepted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="PasskeyInsertOutcome.Inserted"/>; <see cref="PasskeyInsertOutcome.LimitReached"/>;
    /// <see cref="PasskeyInsertOutcome.DuplicateCredential"/> when the credential id is
    /// registered already, to any account; <see cref="PasskeyInsertOutcome.BindCodeLost"/>
    /// when the code was spent or superseded by another request first; or
    /// <see cref="PasskeyInsertOutcome.SessionEnded"/> when the session ended meanwhile.
    /// Nothing is stored, and the code is not spent, unless it is
    /// <see cref="PasskeyInsertOutcome.Inserted"/>.
    /// </returns>
    Task<PasskeyInsertOutcome> TryCreateAsync(
        UserPasskey passkey,
        int maxLive,
        Guid? bindCodeId,
        Guid sessionId,
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
    SessionEnded = 4
}
