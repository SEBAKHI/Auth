# ADR 0005: Signing out in a browser ends every sign-in that began there (single logout, by IAM-side revocation)

## Status

Accepted — 2026-10-19 (OI-155, PR A). Builds on OI-154 (each refresh token records the IdP session it was born from, `RefreshTokens.IdpSessionId`) and [ADR 0004](0004-server-side-sign-out.md) (a server-side application ends the SSO session it holds). No schema change, no contract change. Behind `IdentityProvider:SingleLogoutEnabled`, on by default.

## Context

Ending the single sign-on (SSO) session of a browser used to end that session alone. The platform's own console and accounts application ended their sign-ins at their next refresh (OI-154); every other application signed in through that SSO session kept its session: its refresh token renewed for up to `Jwt:RefreshTokenLifetimeDays` (7 by default), its access token stayed valid. On a shared device, a person signs out and walks away, and the next person opens another application's tab in the same browser, still signed in as the first.

Mature identity providers end the applications' sessions with the user session (single logout, SLO): Keycloak ends every client session of a user session; Microsoft Entra ID and Okta also notify the applications. Since OI-154 this provider knows which refresh chains each SSO session produced, so it can do the same on its side.

## Decision

### 1. An explicit end of an SSO session ends every sign-in born from it

When the SSO session of a browser is ended on purpose, every session of that person whose refresh chain is linked to it ends, applications' and platform apps' alike, with the three moves IAM already makes for one session: the refresh tokens revoked, the session row ended, the session id blacklisted. A token issued without a session row is revoked alone. One service method does it (`ICredentialRevocationService.EndSignInsBornFromIdpSessionAsync`), and returns the sessions it ended.

### 2. Which ends count (owner decision D-155-1 (a))

- A sign-out in that browser: `POST auth/logout` (this device), the sign-out with the first-party refresh cookie, the end-session confirmation page (`POST end-session`, which now ends the SSO session through the same service), and a server-side application's sign-out (ADR 0004; the handler ends that application's own session first, so it keeps its own reason, then the others).
- Another person signing in on that browser, replacing the SSO session. The same person signing in again ends nothing: their chains move to the new session, applications' chains even from a session that went idle (an application's chain never ends with an idle session, and a later sign-out there must still reach it), platform chains only from a live one (OI-154).
- Not an SSO session that goes idle or reaches its maximum. Nothing recorded tells that end from a sign-out (`IdpSessions` has no reason column), and ending applications on idle would bound every application's session by 30 minutes of inactivity.
- Not the user-wide paths (sign out everywhere, a password reset, an account lock-out): they already revoke every refresh token of the person.

### 3. Introspection reads the session blacklist

`POST auth/introspect` answers `active: false` for an access token whose session id is blacklisted, as the platform's own API already rejected it. Independent of the switch: it was a defect on every terminated session.

### 4. Best effort, never cancelled, race-safe

From the cookie lookup on, nothing takes the caller's cancellation; a failure ending one session is logged and the others still end; nothing in it fails the sign-out. A refresh rotating a token while the cascade runs either loses (a pass revoked the token first, so its conditional update matches nothing) or wins and holds the token's row lock until its replacement, which keeps the session id and the IdP session, is committed; a statement that met that lock waits and may still miss the replacement. The read is therefore repeated until two reads in a row find nothing: the first may have waited on a rotation, the second sees its replacement.

### 5. Audit

One `SingleLogoutCompletedEvent` per cascade that ended something, written as a sign-out row (`UserLogout`, `additionalData.signedOutBy = "singleLogout"`, with the trigger, the IdP session and the count). The handler catches and logs its own failure.

### 6. Switch

`IdentityProvider:SingleLogoutEnabled`, on by default (owner decision D-155-3 (a)), read on every sign-out. Off restores the behaviour before this decision exactly, including which chains move when the same person signs in again.

## Consequences

- **The platform's own apps and applications that introspect** see the sign-out at once.
- **An application that validates its access tokens locally** (the SDK does) learns at its next refresh, which is refused, at most one access-token lifetime later (`Jwt:AccessTokenLifetimeMinutes`, 15 by default).
- **Residual risk.**
  - That window, until an application can be notified at the sign-out (Back-Channel Logout, below).
  - A session that idles out or reaches its maximum ends no application (by decision).
  - Chains issued before OI-154 carry no link and end at their own expiry.
  - The cookie lookup reads only SSO sessions not yet revoked (its index is filtered the same way), so once a read has ended a session as idle, a later sign-out or another person's sign-in in that browser no longer names it, and the application sessions born from it end at their own expiry. That is the usual case on a shared computer left unused past the idle limit: the next person opens an application, whose authorize step reads the session and ends it as idle before they sign in. Reaching it needs a lookup that includes revoked sessions, hence an index change, planned with Back-Channel Logout.
  - A second rotation of a just-rotated token would have to reach the database between the cascade's two last reads, which means the client receiving its new token and sending it back within one database round trip. Even then the session id is blacklisted, so its access tokens are refused for an access-token lifetime.
- **Rollback.** Switch it off in the console, with no redeploy; sessions already ended stay ended. Full rollback: the previous API.

## Alternatives considered

- **OpenID Connect Back-Channel Logout 1.0 now** (a signed logout token posted to each application's registered address). Closes the window for applications that validate locally, at the cost of a schema column, a console field, a signer, an outbound sender with its SSRF surface, and a receiver in each application. Decided as the next step (owner decision D-155-2 (a)); the service method returns the ended sessions so it can attach to exactly them.
- **OpenID Connect Front-Channel Logout 1.0** (hidden frames on the signed-out page). Browsers partition or block the third-party cookies those frames depend on, so it often ends nothing. Rejected.
- **Refuse an application's refresh once its SSO session is not live** (Keycloak's SSO Session Idle and Max applied to clients). Also ends applications on idle and lifetime ends, which cannot be told apart from a sign-out, and bounds every application's session by them. Offered as D-155-1 (b), not chosen.

## References

- OpenID Connect RP-Initiated Logout 1.0; OpenID Connect Back-Channel Logout 1.0; OpenID Connect Front-Channel Logout 1.0.
- RFC 7662 (token introspection), §2.2.
- Keycloak, "Server Administration Guide" (user sessions and logout).
- Code: `Auth/Auth.Infrastructure/Authentication/CredentialRevocationService.cs` (`EndSignInsBornFromIdpSessionAsync`); guide: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, "Single logout".
