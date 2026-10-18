# ADR 0004: A server-side application ends the single sign-on session it holds, with its key and its bound refresh token

## Status

Accepted — 2026-10-18 (OI-183, PR A). Builds on [ADR 0003](0003-confidential-server-side-clients.md) (sign-in keys, bound refresh tokens). Adds one endpoint, `POST auth/end-session/server`, advertised as `server_end_session_endpoint`. Nothing changes for an application that does not call it: GET and POST `end-session`, `revoke`, `logout` and the token grants behave as before.

## Context

A person who signs out of a server-side application (a Backend-for-Frontend) has two sessions to end: the application's own, and the single sign-on (SSO) session of the browser they signed in from, which signs them into any application without a password. The application can end the first itself (it revokes its refresh token). Until this decision it could end the second only by sending the browser to the end-session endpoint, which never signs anyone out on its own: with no `id_token_hint` (this provider issues no id tokens), it cannot tell the application's request from any page that loads the address, so it asks the person (OpenID Connect RP-Initiated Logout 1.0, §2 and §6: without a valid hint the provider must ask, or a logout request becomes a denial of service any site can trigger).

That question comes after the application has already signed the person out. A person who answers "stay signed in", or closes the page, keeps the SSO session alive in that browser, and on a shared device the next person to open the application is signed in as them without a password.

Mature providers that end the SSO session from a server do it with an authenticated confidential client and a handle naming exactly one session: Keycloak's logout endpoint called directly with the refresh token and the client's credentials (which it calls a legacy, non-standard form), Auth0's Management API session deletion by `sid`, PingFederate's back-channel session revocation by session id (advertised under its own metadata name, `ping_revoked_sris_endpoint`). OpenID Connect Back-Channel Logout 1.0 goes the other way (provider to application) and is the shape of single logout, not of this.

This provider already has both parts: since ADR 0003 a server-side application authenticates with a sign-in key and its refresh tokens are bound to that key, and since OI-154 every refresh token records the IdP session it was born from (`RefreshTokens.IdpSessionId`).

## Decision

### 1. A dedicated endpoint, advertised in discovery

`POST api/v1/auth/end-session/server`, form-encoded with the token endpoint's field names: `refresh_token`, `client_id` (optional), `client_assertion_type`, `client_assertion`. The discovery document names it `server_end_session_endpoint`. It is not a registered metadata name; a client that finds no such member is talking to a provider without it and keeps sending the browser to `end_session_endpoint`. A new path, so an older API answers 404 rather than the 204 its cookie-based POST `end-session` gives a request with no cookie.

### 2. What proves the request

An assertion signed by an active **sign-in** key of a live application (ADR 0003 §3, unchanged; the `jti` is spent), and a stored refresh token that is **bound** and belongs to that application. The client is authenticated before the token is looked up. The endpoint reads no cookie and needs no Origin, so no page in a browser can make it act: there is nothing ambient to forge. A thief of the token without the key ends nothing; a thief of the key without the token names no session.

### 3. What it ends

Only a live chain is acted on: the presented token while it is live, or, when it was rotated within the application replay grace (`Jwt:ApplicationRefreshReplayGraceSeconds`), its live replacement. Then, after which the caller cannot cancel:
1. the IdP session the chain is linked to (by id);
2. the application session, as revocation ends it (every refresh token of the session, the row, and the session id blacklisted so the access tokens already out stop).

Nothing else. The person's other browsers keep their IdP sessions (owner decision D2 (a)). Other applications' tokens are left as they are: ending them is single logout (OI-155). The platform's own sign-ins born with the IdP session end at their next refresh, as after any end of it (OI-154).

A stale presentation (rotated outside the grace, revoked, expired) ends nothing and is never treated as reuse: this endpoint issues nothing.

### 4. The answer says whether the browser is signed out

200 `{"sso_session_ended": bool}` with `Cache-Control: no-store`. True when the linked IdP session no longer signs its browser in, ended now or already. False when the token carries no link (issued before the link existed): the application then sends the browser to `end_session_endpoint`, which asks only if that browser still holds a session. Refusals: `Auth.InvalidClient` (the client did not authenticate), and one `Auth.InvalidToken` for an unknown token, another application's, and a public client's, so an application learns nothing about a token that is not its own.

### 5. Budget and audit

The `token` rate-limit policy, per address, at the API and on its own gateway route (`end-session-server-route`), beside revocation: one call per sign-out, and nothing at it can be guessed. Not classified per application (OI-178): the classification keys the key purpose on `grant_type`, which this form has none of, and sign-outs are far fewer than renewals. Each call that ended something is audited as `user.logout` with the application, the session and whether the SSO session ended; the audit handler logs its own failure and never throws.

## Consequences

- **A server-side application in confidential mode can sign a person out of its session and of the browser's SSO session at once**, with no confirmation screen, and land on its own page. The person's next sign-in in that browser asks for the password.
- **Fallback is the application's to keep.** Against a provider without the endpoint, on any refusal, error, timeout or 429, or on `sso_session_ended: false`, the application revokes and sends the browser to `end_session_endpoint` as before. The deploy order of the provider and the application is therefore free.
- **Public mode is unchanged.** A token issued without client authentication is refused here; it becomes bound at its first authenticated refresh.
- **Residual risk.** An application's key and one of its bound refresh tokens, stolen together, can end that person's SSO session in one browser; whoever holds both already holds the person's session at that application. Other applications already open in the same browser stay signed in until their own sessions end (single logout, OI-155).
- **Rollback.** An older API lacks the member and the path; applications fall back to the confirmation page. No data to undo.

## Alternatives considered

- **A parameter on `POST auth/revoke`.** No new route, but RFC 7009 §2.2 answers 200 even when nothing was found, so the application could not learn whether the SSO session ended without bending that contract; an older provider would ignore the parameter and also answer 200; and it would decide OI-176 (an assertion at revocation) in passing. Rejected.
- **Id tokens and `id_token_hint` at the end-session endpoint.** The standard route, and it would serve browser-only applications too. Deferred by the owner: id tokens are a larger change (issuance, `sid`, nonce, discovery, every library's expectations). It remains the later route; this endpoint does not preclude it.
- **A per-application "sign out without asking" on GET end-session.** Brings back logout CSRF: any site could sign people out by loading the address. Rejected.
- **Ending every IdP session of the person.** The person asked to sign out of this device; it would also give an application's key the power to sign a person out everywhere. Rejected (owner decision D2 (a)).

## References

- OpenID Connect RP-Initiated Logout 1.0, §2, §6; OpenID Connect Back-Channel Logout 1.0, §2.5.
- RFC 7009 (token revocation), §2.1, §2.2; RFC 7523 (JWT client authentication); RFC 9700 (OAuth 2.0 Security BCP).
- Keycloak, "Securing applications and services with OpenID Connect", section "Logout endpoint".
- PingFederate, "Session Revocation API endpoint" (`ping_revoked_sris_endpoint`).
- Code: `Auth/Auth.Application/Features/Authentication/ServerSignOut/ServerSignOutCommandHandler.cs`; guide: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, "Signing out from your server".
