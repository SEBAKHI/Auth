namespace Auth.Application.Interfaces;

/// <summary>
/// Derives the opaque handle a client holds for a pending self-registration.
/// </summary>
/// <remarks>
/// The handle is a keyed digest of the normalized address, so it is the same
/// value every time an address starts a registration — before the row exists,
/// while it is live, and after it was consumed and a new row was written. That
/// stability is the point: if the handle were the row's key, the moment it
/// changed would tell anyone who kept polling that the address had acquired an
/// account through some door in between. It is not a secret and proves
/// nothing; the code that reached the mailbox is the proof.
/// </remarks>
public interface IPendingRegistrationHandle
{
    /// <summary>
    /// The handle for <paramref name="normalizedEmail"/> (the upper-case form
    /// stored in Users.NormalizedEmail, so two spellings of one address share
    /// one handle).
    /// </summary>
    string For(string normalizedEmail);
}
