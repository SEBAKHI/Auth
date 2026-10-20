# ADR 0005: Signing out in a browser ends every sign-in that began there (single logout, by IAM-side revocation)

## Status

Accepted — 2026-10-19 (OI-155, PR A). Builds on OI-154 (each refresh token records the IdP session it was born from, `RefreshTokens.IdpSessionId`) and [ADR 0004](0004-server-side-sign-out.md) (a server-side application ends the SSO session it holds). No schema change, no contract change. Behind `IdentityProvider:SingleLogoutEnabled`, on by default.

Amended — 2026-10-19 (OI-155, PR B1): §7, §8 and §9. One index change (`IX_IdpSessions_TokenHash` loses its filter), no contract change.

Amended — 2026-10-20 (OI-155, PR C): §10, the tree model (owner decision D-155-9, which supersedes D-155-1 (a)). It supersedes §2's third bullet and its "even from a session that went idle" clause, two consequences in Consequences, and moves one alternative to chosen. Behind `IdentityProvider:ApplicationSessionsEndWithSsoSession`, on by default. No schema change, no contract change.

## Context

Ending the single sign-on (SSO) session of a browser used to end that session alone. The platform's own console and accounts application ended their sign-ins at their next refresh (OI-154); every other application signed in through that SSO session kept its session: its refresh token renewed for up to `Jwt:RefreshTokenLifetimeDays` (7 by default), its access token stayed valid. On a shared device, a person signs out and walks away, and the next person opens another application's tab in the same browser, still signed in as the first.

Mature identity providers end the applications' sessions with the user session (single logout, SLO): Keycloak ends every client session of a user session; Microsoft Entra ID and Okta also notify the applications. Since OI-154 this provider knows which refresh chains each SSO session produced, so it can do the same on its side.

## Decision

### 1. An explicit end of an SSO session ends every sign-in born from it

When the SSO session of a browser is ended on purpose, every session of that person whose refresh chain is linked to it ends, applications' and platform apps' alike, with the three moves IAM already makes for one session: the refresh tokens revoked, the session row ended, the session id blacklisted. A token issued without a session row is revoked alone. One service method does it (`ICredentialRevocationService.EndSignInsBornFromIdpSessionAsync`), and returns the sessions it ended.

### 2. Which ends count (owner decision D-155-1 (a))

- A sign-out in that browser: `POST auth/logout` (this device; the cascade runs only when the browser's SSO session belongs to the signed-in user, so a console tab one person left open does not end the applications of the person who signed in since), the sign-out with the first-party refresh cookie, the end-session confirmation page (`POST end-session`, which now ends the SSO session through the same service), and a server-side application's sign-out (ADR 0004; the handler ends that application's own session first, so it keeps its own reason, then the others).
- Another person signing in on that browser, replacing the SSO session. The same person signing in again ends nothing: their chains move to the new session, from a live one every chain. *Superseded by §10 while its switch is on:* applications' chains even from a session that went idle (an application's chain never ends with an idle session, and a later sign-out there must still reach it), platform chains only from a live one (OI-154).
- Not an SSO session that goes idle or reaches its maximum: nothing recorded tells that end from a sign-out (`IdpSessions` has no reason column), so no cascade runs for it. *Superseded by §10 while its switch is on:* ending applications on idle would bound every application's session by 30 minutes of inactivity, so an idle or lifetime end ended no application. Under §10 the sessions born from it end at their next refresh instead.
- Not the user-wide paths (sign out everywhere, a password reset, an account lock-out): they already revoke every refresh token of the person.

### 3. Introspection reads the session blacklist

`POST auth/introspect` answers `active: false` for an access token whose session id is blacklisted, as the platform's own API already rejected it. Independent of the switch: it was a defect on every terminated session. It hardens a platform endpoint for platform callers and does nothing for applications: introspection validates the platform audience only, so an application's access token (its audience is the application's code) is inactive there whether its session lives or not. An application learns of a sign-out at its next refresh.

### 4. Best effort, never cancelled, race-safe

From the first database call of a sign-out on (the cookie lookup, and the refresh-token lookups of the two logout handlers), nothing takes the caller's cancellation; a failure ending one session is logged and the others still end; a session counts as ended once its refresh tokens are revoked, even when ending its row then fails; a failed read is retried within the cap; nothing in it fails the sign-out. A refresh rotating a token while the cascade runs either loses (a pass revoked the token first, so its conditional update matches nothing) or wins and holds the token's row lock until its replacement, which keeps the session id and the IdP session, is committed; a statement that met that lock waits and may still miss the replacement. The read is therefore repeated until two reads in a row find nothing: the first may have waited on a rotation, the second sees its replacement.

### 5. Audit

One `SingleLogoutCompletedEvent` per cascade that ended something, written as a sign-out row (`UserLogout`, `additionalData.signedOutBy = "singleLogout"`, with the trigger, the IdP session and the count). The handler catches and logs its own failure.

### 6. Switch

`IdentityProvider:SingleLogoutEnabled`, on by default (owner decision D-155-3 (a)), read on every sign-out. Off restores the behaviour before this decision, including which chains move when the same person signs in again, except that a sign-out's own SSO-session lookup and revocation no longer stop for a caller that left, and that the token endpoint still refuses a code whose SSO session has ended (§8, which the switch does not govern).

### 7. An SSO session a read already ended as idle is still reached (PR B1)

A session that went idle is revoked when something reads it (or in bulk, when an administrator changes the idle limit), and on a shared computer the commonest readers are the authorize step of the next person's sign-in, which runs before they sign in, and the console's or the accounts application's own refresh, which is refused and then deletes that application's refresh cookie. The two explicit ends (a sign-out by cookie, and a sign-in replacing the browser's session) therefore look the session up by its cookie live first and then, with the switch on, revoked too (`IIdpSessionRepository.GetByTokenHashIncludingRevokedAsync`); `IX_IdpSessions_TokenHash` has no filter, so both lookups seek. A session found revoked is not revoked again, and its chains are reached as in §2: another person's sign-in ends them, the same person's sign-in moves their applications' chains. Every reader that signs a browser in keeps the live-only lookup, so a revoked cookie still signs nobody in. Off, a revoked session is not looked up at all.

What reaches an idle-ended session in practice: another person's sign-in in that browser; the same person's sign-in (the move); the console's or the accounts application's sign-out (§9); the confirmation page, only if it was open before the session ended (GET end-session shows it for a live session only); the bearer sign-out, only while the console's access token is still valid, so only where `Jwt:AccessTokenLifetimeMinutes` is at least the idle limit.

### 8. A code redeemed after the sign-out mints nothing (PR B1)

The token endpoint refuses a code whose SSO session is revoked or gone (`Auth.AuthorizationCodeInvalid`), whatever the switch, and the code stays consumed: a sign-in completed within the code's lifetime after a sign-out would otherwise start an application session that the sign-out never saw. The session is read twice: before the sign-in is written, and again after; when the second read finds it ended, the exchange ends the session it just issued and returns no tokens. Write, then read, on both sides: the sign-out revokes the session and then reads its chains, the exchange writes the chain and then reads the session, and each statement commits on its own, so one of the two reads sees the other's write. The same person's sign-in, which moves the chains before it revokes the old session, moves again after the revocation when its read finds a chain written in between. A code issued before codes recorded their SSO session (OI-154) is redeemed as before. The rule is "revoked or gone", not the reader's full live rule: an SSO session going idle inside a code's lifetime is not a realistic case. A sign-in that replaces the session, the same person's included, also revokes it, so when two applications sign the same person in at the same moment one code can be refused; that application starts a new sign-in, which needs no password.

### 9. The console's sign-out after the idle limit (PR B1, owner decision D-88-1 (a))

After the idle limit the console's access token has expired, its refresh is refused (that refusal is the read that ends the SSO session) and the refresh cookie is deleted, so its sign-out reaches `POST auth/logout/cookie` with no refresh cookie, only the SSO cookie and the console's own session id. That request now ends the SSO session its cookie names, live or revoked, and with the switch on the sign-ins born from it, only when that session id is a session of the same person (`ICredentialRevocationService.RevokeIdpSessionOfSessionOwnerAsync`); then the SSO cookie is deleted. Another person's session id, an unknown one, or none ends nothing, so a tab one person left open ends nothing of the person who signed in since. Off, nothing is looked up, as before. The endpoint keeps its first-party Origin check; its rate limit is the gateway's auth route, as for every `auth/` path.

### 10. An application's sign-in ends with the SSO session it was born from (PR C, owner decision D-155-9)

The tree model. Since PR A an application's sign-in outlived its SSO session by decision (D-155-1 (a)), so single logout had to find each application chain at the sign-out, and every timing case of §4, §7 and §8 came from that. Keycloak binds client sessions to the user session the same way (SSO Session Idle and Max), and Duende IdentityServer coordinates client lifetimes with its server-side session.

- **The refusal.** An application's refresh is refused (`Auth.RefreshTokenRevoked`) once the SSO session its chain names no longer signs the browser in: revoked, idle, past its maximum, or its row gone. It ends the chain as a platform app's own refresh already did (OI-154): the session's refresh tokens revoked with `IdpSessionEnded`, its row ended, its id blacklisted. Same place in the refresh, same error. A read of the SSO session that fails fails that refresh and ends nothing; the next one decides. No new event or audit row: the reason is on the revoked tokens, as for a platform app.
- **The sliding half.** Each accepted refresh of an application already records a use of its SSO session (OI-154, D-154-2 (b)), and that write never revives a session that is revoked, idle or past its maximum. So a person at work in an application keeps the session alive, and a refused refresh records nothing.
- **The same person signing in again.** Every chain moves from a previous session that still signs the browser in, as before. Nothing moves from one that does not: its applications' chains are refused at their next refresh, as its platform chains already were, and a new sign-in does not bring them back. The second move (§8) follows the first.
- **The settings pair.** An application counts a use only when it refreshes, about once per access-token lifetime, so the console refuses a save or a reset that leaves `IdentityProvider:IdpBrowserSessionIdleMinutes` above 0 and below twice `Jwt:AccessTokenLifetimeMinutes` (owner decision D-C-1 (a); `SystemSettings.InvalidFieldValue`, naming the other setting and its value). Judged on the values after the write, from either section. Startup does not check it, so an existing configuration keeps starting.
- **Unchanged.** A chain with no SSO session (`IdpSessionId` NULL: issued before the link existed, a sign-in without an Origin, a failed link) refreshes as before. The code exchange keeps §8's rule: a code is issued from a session the authorize just used and lives `AuthorizationCodeLifetimeSeconds`, so an idle session at the exchange does not arise, and one expiring inside that window is refused at the chain's first refresh.
- **Switch.** `IdentityProvider:ApplicationSessionsEndWithSsoSession`, on by default, read on every refresh and every sign-in that replaces a session. Separate from `SingleLogoutEnabled`: turning either off leaves the other as it is. Off restores the behaviour of §2 before this section: an application's chain lives its own refresh lifetime whatever its SSO session does, and moves with the same person's sign-in even from a session that went idle.

What the person sees: without "Stay signed in", an application's sign-in ends after between (idle limit − one access-token lifetime) and the idle limit without use of any application or the console, and at the browser session's maximum (`IdpBrowserSessionLifetimeHours`) even in use; with it, there is no idle limit and the sign-in ends at the SSO session's lifetime (`IdpSessionLifetimeDays`).

## Consequences

- **The platform's own apps** (console, accounts) see the sign-out at once: their API reads the session blacklist.
- **An application** validates its access tokens itself (the SDK does) and learns at its next refresh, which is refused, at most one access-token lifetime later (`Jwt:AccessTokenLifetimeMinutes`, 15 by default). Introspection does not shorten that window (§3); Back-Channel Logout will, for an application that registers an address.
- **A server-side application's sign-out can answer `sso_session_ended: true` where it answered `false` before** (ADR 0004 §4): when the same person signed in again in that browser after their SSO session went idle, the application's chain moved to the new session, so the sign-out ends the browser's current session. The answer is right: that session did end. *With §10's switch on this case no longer arises:* nothing moves from a session that went idle.
- **Residual risk.**
  - That window, until an application can be notified at the sign-out (Back-Channel Logout, below).
  - *Superseded by §10 while its switch is on:* a session that idles out or reaches its maximum ended no application (by decision D-155-1 (a)). Under §10 it ends them at their next refresh, so the timing cases above last at most one access-token lifetime, after which the refresh is refused anyway.
  - `refresh_expires_in`, and a rotated refresh token's own expiry, still count `Jwt:RefreshTokenLifetimeDays`, although under §10 the chain ends at the SSO session's maximum at the latest. It is an upper bound: an application that schedules by it is refused earlier, which it must already handle.
  - Chains issued before OI-154 carry no link and end at their own expiry.
  - A revoked SSO session's row is deleted by the retention sweep (`DataRetention:IdpSessionDays`, 30 days by default, §7); a cookie a browser still holds after that names nothing, and the sessions born from it end at their own expiry. Until PR B1, a session a read had ended as idle was already out of reach (the cookie lookup and its index saw only sessions not revoked); §7 closed that.
  - An application's own sign-out through the browser (`GET end-session`) after the SSO session already ended shows no confirmation and ends nothing more: that page ends only a live session.
  - A code redeemed while the person signs out, whose second read of the SSO session fails (a database fault), keeps the session it issued; the failure is logged. The same holds for the same person's second move.
  - A cookie value that is no longer live, but whose row is still kept (§7), can still end the sign-ins born from that session: through the end-session confirmation page, as the previous cookie of a sign-in, or with the console's sign-out together with a session id of its owner (§9). Whoever holds such a value can sign its owner out of those applications, and nothing more; while the value was live it could do more.
  - A second rotation of a just-rotated token would have to reach the database between the cascade's two last reads, which means the client receiving its new token and sending it back within one database round trip. Even then the session id is blacklisted, so its access tokens are refused for an access-token lifetime.
- **Rollback.** Switch it off in the console, with no redeploy; sessions already ended stay ended. The refusal of a code whose SSO session ended (§8) stays: only the previous API undoes it. §10 rolls back the same way through its own switch; the pair rule on saving settings stays with it. Full rollback: the previous API.

## Alternatives considered

- **OpenID Connect Back-Channel Logout 1.0 now** (a signed logout token posted to each application's registered address). Closes the window for applications that validate locally, at the cost of a schema column, a console field, a signer, an outbound sender with its SSRF surface, and a receiver in each application. Decided as the next step (owner decision D-155-2 (a)); the service method returns the ended sessions so it can attach to exactly them.
- **OpenID Connect Front-Channel Logout 1.0** (hidden frames on the signed-out page). Browsers partition or block the third-party cookies those frames depend on, so it often ends nothing. Rejected.
- **Refuse an application's refresh once its SSO session is not live** (Keycloak's SSO Session Idle and Max applied to clients). Also ends applications on idle and lifetime ends, which cannot be told apart from a sign-out, and bounds every application's session by them. Offered as D-155-1 (b), not chosen at first; chosen later as the tree model (D-155-9, §10), with the application's own refreshes counting as uses so a session in use does not idle out.

## References

- OpenID Connect RP-Initiated Logout 1.0; OpenID Connect Back-Channel Logout 1.0; OpenID Connect Front-Channel Logout 1.0.
- RFC 7662 (token introspection), §2.2.
- Keycloak, "Server Administration Guide" (user sessions and logout).
- Code: `Auth/Auth.Infrastructure/Authentication/CredentialRevocationService.cs` (`EndSignInsBornFromIdpSessionAsync`); guide: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, "Single logout".
