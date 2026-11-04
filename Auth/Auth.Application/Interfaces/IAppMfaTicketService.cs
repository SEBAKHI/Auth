using Auth.Domain.Enums;

namespace Auth.Application.Interfaces;

/// <summary>
/// Issues and reads the signed record of an application's two-step redirect (S23, AM8
/// (8-1)): the authorize endpoint sent this person to sign in for a reason, and the ticket
/// lets it recognise a return that brought no progress.
/// </summary>
/// <remarks>
/// A ticket is signed over the client, the user, the reason and a fingerprint of the
/// request's PKCE <c>code_challenge</c>, under the keyring's own purpose. So it answers
/// only the request that raised it, only for the person it was raised for, and only for
/// that reason: a new request of the application (a new challenge) is a new redirect,
/// never a refusal, and a different reason is progress. It lives in its own cookie,
/// <c>__Host-auth_appmfa</c>, beside the step-up ticket, which it never reads or replaces.
/// Nothing secret is inside: it is a record the server vouches for, not a credential, and
/// it never grants anything. Its only effect is ending a loop with <c>access_denied</c>.
/// </remarks>
public interface IAppMfaTicketService
{
    /// <summary>
    /// Issues a ticket recording that <paramref name="userId"/> was sent to sign in for
    /// <paramref name="reason"/> by <paramref name="clientId"/>'s request
    /// <paramref name="codeChallenge"/> at <paramref name="issuedAtUtc"/>.
    /// </summary>
    string Issue(
        string clientId, Guid userId, ApplicationMfaRequirement reason, string codeChallenge, DateTime issuedAtUtc);

    /// <summary>
    /// Reads a ticket raised for this client, this user and this request, within its
    /// lifetime.
    /// </summary>
    /// <param name="ticket">The cookie value, or null when the browser sent none.</param>
    /// <param name="clientId">The client of the current request.</param>
    /// <param name="userId">The user the current session signs in.</param>
    /// <param name="codeChallenge">The current request's PKCE <c>code_challenge</c>.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="reason">The reason the ticket recorded, when it was read.</param>
    /// <returns>
    /// True only when the signature verifies and every bound field matches; absent,
    /// malformed, forged, stale, or raised for another client, person or request is false.
    /// </returns>
    bool TryRead(
        string? ticket,
        string clientId,
        Guid userId,
        string? codeChallenge,
        DateTime nowUtc,
        out ApplicationMfaRequirement reason);

    /// <summary>
    /// Whether the ticket was raised by this client's request, for whoever it was raised:
    /// the request is over (a code, or a final error), so the ticket it raised goes.
    /// </summary>
    bool BelongsTo(string? ticket, string clientId, string? codeChallenge, DateTime nowUtc);
}
