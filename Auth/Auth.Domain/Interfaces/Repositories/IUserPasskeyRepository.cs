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
    /// <paramref name="maxLive"/> live ones of its RP ID, in one transaction with spending the
    /// emailed first-factor code the bind was checked with, when it needed one (X02).
    /// </summary>
    /// <param name="passkey">The passkey.</param>
    /// <param name="maxLive">The most live passkeys the account may hold.</param>
    /// <param name="bindCodeId">The emailed code the bind was checked with, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="PasskeyInsertOutcome.Inserted"/>; <see cref="PasskeyInsertOutcome.LimitReached"/>;
    /// <see cref="PasskeyInsertOutcome.DuplicateCredential"/> when the credential id is
    /// registered already, to any account; or <see cref="PasskeyInsertOutcome.BindCodeLost"/>
    /// when the code was spent or superseded by another request first. Nothing is stored
    /// unless it is <see cref="PasskeyInsertOutcome.Inserted"/>.
    /// </returns>
    Task<PasskeyInsertOutcome> TryCreateAsync(
        UserPasskey passkey,
        int maxLive,
        Guid? bindCodeId,
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
    BindCodeLost = 3
}
