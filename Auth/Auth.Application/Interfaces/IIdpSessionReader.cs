using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// What a read of the IdP session cookie may write (OI-154).
/// </summary>
public enum IdpSessionReadMode
{
    /// <summary>
    /// Reads only, for a query: nothing is written, not even the end of a session
    /// found idle.
    /// </summary>
    Inspect,

    /// <summary>
    /// Ends a session found idle, so raising the limit later never brings it back,
    /// and records no use.
    /// </summary>
    Check,

    /// <summary>
    /// As <see cref="Check"/>, and records this read as the session's use: the
    /// browser's IdP session is signing the person in to something.
    /// </summary>
    Use
}

/// <summary>
/// The one place that turns the IdP session cookie into a live session (OI-154).
/// Every reader of the cookie goes through it, so "valid" means the same thing
/// everywhere: not revoked, not past its lifetime, and, for a browser session, not
/// idle (<see cref="IdpSession.IsValidAt"/>, with the clock and the configured
/// limit).
/// </summary>
public interface IIdpSessionReader
{
    /// <summary>
    /// The live session the cookie names, or null: no cookie, an unknown or
    /// revoked token, a session past its lifetime, or one that went idle.
    /// </summary>
    /// <param name="idpSessionToken">The plain cookie value, if any.</param>
    /// <param name="mode">What this read may write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdpSession?> FindLiveAsync(
        string? idpSessionToken, IdpSessionReadMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Records a use of the session by its id: the authorize endpoint is issuing
    /// a code from it, or a refresh token born from it was just accepted (owner
    /// decision D-154-2 (b)). Writes nothing to a session that is revoked, past
    /// its lifetime or idle, so it never brings one back.
    /// </summary>
    /// <returns>True when the use was recorded; false when the session was dead.</returns>
    Task<bool> RecordUseAsync(Guid idpSessionId, CancellationToken cancellationToken);
}
