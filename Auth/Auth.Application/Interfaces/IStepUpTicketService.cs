namespace Auth.Application.Interfaces;

/// <summary>
/// Issues and validates the signed record of a step-up demand.
/// </summary>
/// <remarks>
/// The authorize endpoint and the login page are two separate requests with a
/// browser round trip in between, so a <c>prompt=login</c> demand made on the
/// first has to survive to the second for the server to know it was answered.
/// A ticket carries the moment the demand was made and the client it was made
/// for, signed with the server's HMAC key so a browser can neither forge one
/// nor back-date one.
/// <para>
/// This replaces trusting the client to delete the parameter. Deleting the
/// demand satisfied it by removing it, which meant a fresh authentication was
/// never actually proved — anyone holding a live session cookie could strip the
/// parameter by hand and be issued a code against the old session.
/// </para>
/// </remarks>
public interface IStepUpTicketService
{
    /// <summary>
    /// Issues a ticket recording that step-up was demanded for
    /// <paramref name="clientId"/> at <paramref name="demandedAtUtc"/>.
    /// </summary>
    string Issue(string clientId, DateTime demandedAtUtc);

    /// <summary>
    /// Validates a ticket's signature, its client and its freshness.
    /// </summary>
    /// <param name="ticket">The cookie value, or null when the browser sent none.</param>
    /// <param name="clientId">The client the current request is for.</param>
    /// <param name="nowUtc">The current time, for lifetime evaluation.</param>
    /// <param name="lifetime">How long a ticket stays answerable.</param>
    /// <param name="demandedAtUtc">The moment step-up was demanded, when valid.</param>
    /// <returns>
    /// True only when the signature verifies, the client matches and the ticket
    /// has not expired. Anything else — absent, malformed, forged, stale, or
    /// issued for a different client — is false, which re-demands step-up. The
    /// failure direction is deliberate: an unreadable ticket must never be able
    /// to satisfy a demand.
    /// </returns>
    bool TryValidate(
        string? ticket,
        string clientId,
        DateTime nowUtc,
        TimeSpan lifetime,
        out DateTime demandedAtUtc);
}
