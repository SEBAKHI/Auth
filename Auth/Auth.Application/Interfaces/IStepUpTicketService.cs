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
/// The signature also covers the PKCE <c>code_challenge</c> of the authorize
/// request that raised the demand (OI-150), so the ticket answers that request
/// and no other. Before, it answered any later request of the same client on the
/// same browser within its lifetime: someone who left a sign-in unfinished on a
/// shared computer let the next person's sign-in be answered by their session.
/// The challenge is mandatory at authorize, a conformant client makes a new one
/// for every request, and every return of the same request carries the same
/// one (the URL holds it, and a stored request replays it).
/// </para>
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
    /// <paramref name="clientId"/>'s request <paramref name="codeChallenge"/> at
    /// <paramref name="demandedAtUtc"/>.
    /// </summary>
    string Issue(string clientId, string codeChallenge, DateTime demandedAtUtc);

    /// <summary>
    /// Validates a ticket's signature, its client, its request and its freshness.
    /// </summary>
    /// <param name="ticket">The cookie value, or null when the browser sent none.</param>
    /// <param name="clientId">The client the current request is for.</param>
    /// <param name="codeChallenge">The current request's PKCE <c>code_challenge</c>.</param>
    /// <param name="nowUtc">The current time, for lifetime evaluation.</param>
    /// <param name="lifetime">How long a ticket stays answerable.</param>
    /// <param name="demandedAtUtc">The moment step-up was demanded, when valid.</param>
    /// <returns>
    /// True only when the signature verifies for this client and this challenge
    /// and the ticket has not expired. Anything else — absent, malformed, forged,
    /// stale, issued for a different client or a different request, or written
    /// before the challenge was signed — is false, which re-demands step-up. The
    /// failure direction is deliberate: an unreadable ticket must never be able
    /// to satisfy a demand.
    /// </returns>
    bool TryValidate(
        string? ticket,
        string clientId,
        string? codeChallenge,
        DateTime nowUtc,
        TimeSpan lifetime,
        out DateTime demandedAtUtc);
}
