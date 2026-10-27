using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Shared service that builds a LoginResponse with tokens and user info.
/// Used by both LoginCommandHandler and ExternalLoginCommandHandler.
/// </summary>
public interface ILoginResponseBuilder
{
    /// <summary>
    /// Builds a complete login response: generates tokens, stores refresh token, records login attempt.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="userAgent">The client's raw user-agent header, if it sent one.</param>
    /// <param name="deviceId">
    /// The client-supplied per-browser identifier, if it sent one. Recognition
    /// only — it is forgeable and is never read as an authorization input. Null
    /// for callers that have no browser storage to keep it in, such as the OAuth
    /// token endpoint.
    /// </param>
    /// <param name="authenticationMethods">
    /// What this request verified, written on the session rows and into the
    /// token's <c>amr</c> and <c>auth_time</c>, and what the platform-administrator
    /// two-factor policy reads. Required, with no default: every sign-in exit must
    /// say what it proved. Only the authorization-code exchange passes
    /// <see cref="AuthenticationMethods.Unknown"/>: an application token carries no
    /// platform authority and claims no authentication time it cannot show.
    /// </param>
    /// <param name="passwordProven">
    /// Whether this sign-in proved the account's password in the same sign-in: the
    /// password form (including the familiar-source door), the second-factor step
    /// that completes a password sign-in, or a password recovery. Only then does the
    /// success lift an automatic lock and start a new failure count. Required, with no
    /// default, like <paramref name="authenticationMethods"/>: a provider, an emailed
    /// code or a token exchange passes false, and lifts nothing (owner decision G8;
    /// <c>BuildAsyncCallersRepairGuardTests</c> names who passes true).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="establishIdpSession">
    /// Whether to also mint an IdP SSO session (carried on
    /// <see cref="LoginResponse.IdpSessionToken"/> for the controller to move
    /// into the session cookie). Interactive logins pass true; the OAuth token
    /// endpoint passes false — no browser is present there to receive a cookie.
    /// </param>
    /// <param name="audience">
    /// The access token's audience (the requesting app's client id for the OAuth
    /// flow); null uses the platform default. Also recorded on the refresh token
    /// (via <paramref name="applicationId"/>) so refreshes keep the same audience.
    /// </param>
    /// <param name="applicationId">The application the tokens are scoped to, if any.</param>
    /// <param name="scope">
    /// The scopes granted to that application, in canonical text. Written into the
    /// access token's "scope" claim, stored on the refresh token so refreshes keep
    /// (or narrow) it, and returned as <see cref="TokenResponse.Scope"/>. Null for
    /// platform sign-ins, which carry no scope.
    /// </param>
    /// <param name="twoFactorChallengeId">
    /// The challenge whose ceremony this sign-in is completing. When set, the
    /// outcome settles that ceremony's existing row rather than inserting a new
    /// one — a two-factor sign-in spans two requests and must still leave one
    /// row. Null for sign-ins that never demanded a second factor.
    /// </param>
    /// <param name="persistent">
    /// Whether the person ticked "Stay signed in" (OI-154): the IdP session minted
    /// here then lasts <c>IdpSessionLifetimeDays</c> with no idle limit, and its
    /// cookie and the app's refresh cookie outlive the browser. False by default,
    /// so an exit that forgets to pass it fails closed, to a browser session. Only
    /// the password, provider, emailed-code (OI-215) and second-factor exits pass
    /// the person's choice; every other exit mints a browser session. The administrator's
    /// <c>IdentityProvider:StaySignedInMode</c> (OI-165) is applied to it here, and
    /// only here: under "always a browser session" a tick is ignored, never
    /// refused, and under "always stay signed in" every exit that mints a session
    /// mints a persistent one.
    /// </param>
    /// <param name="linkedIdpSessionId">
    /// The IdP session the refresh token's chain is born from, when this call
    /// mints none: the code exchange passes the session its code was issued from.
    /// A call that mints one links to it instead. An accepted refresh of the chain
    /// then counts as that session's use (D-154-2 (b)).
    /// </param>
    /// <param name="requiresClientAuthentication">
    /// True to bind the refresh token to its application's sign-in key (OI-171): the code
    /// exchange passes true when the request authenticated the application. Every other
    /// caller leaves it false.
    /// </param>
    /// <returns>
    /// The login response with tokens and user info, or
    /// <c>Session.MaxSessionsReached</c> when the account is at its concurrent
    /// session limit and the limit is configured to refuse rather than evict.
    /// That refusal is the reason this returns <see cref="ErrorOr{T}"/> at all:
    /// it has to be decided before a single token is minted, which no caller can
    /// do on its own without duplicating the check six times.
    /// </returns>
    Task<ErrorOr<LoginResponse>> BuildAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        AuthenticationMethods authenticationMethods,
        bool passwordProven,
        CancellationToken cancellationToken,
        bool establishIdpSession = true,
        string? audience = null,
        Guid? applicationId = null,
        string? scope = null,
        Guid? twoFactorChallengeId = null,
        bool persistent = false,
        Guid? linkedIdpSessionId = null,
        bool requiresClientAuthentication = false);
}
