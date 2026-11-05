namespace Auth.Application.Interfaces;

/// <summary>
/// Whether the database holds the passkey schema (S20): the <c>UserPasskeys</c> and
/// <c>WebAuthnChallenges</c> tables and the <c>Users.WebAuthnUserHandle</c> column.
/// </summary>
/// <remarks>
/// The DACPAC is published before the code that reads it, but the two have been deployed out
/// of order before. Without the schema, the code that reads passkeys behaves exactly as
/// before S20: no passkey counts as a factor, none is listed, none is revoked, and the
/// feature stays off. A statement that named a missing table would fail every sign-in of a
/// platform administrator instead.
/// </remarks>
public interface IPasskeySchema
{
    /// <summary>Whether the passkey schema is present.</summary>
    Task<bool> IsPresentAsync(CancellationToken cancellationToken);
}
