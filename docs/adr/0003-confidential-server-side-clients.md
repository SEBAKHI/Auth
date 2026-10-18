# ADR 0003: Server-side applications authenticate with private_key_jwt on the user grants, and their refresh tokens are bound to the key

## Status

Accepted — 2026-10-16 (OI-171, PR B). Supersedes the bullet "This grant only" of [ADR 0002](0002-machine-clients.md). Adds a second redirect list, a purpose to application keys, and client authentication to the `authorization_code` and `refresh_token` grants. Nothing changes for an application that stays public, except two refusals (Consequences, "The two intended behaviour changes").

## Context

A Backend-for-Frontend (BFF) is a server that runs the authorization-code flow on behalf of a browser application, keeps the access and refresh tokens on the server, and gives the browser only its own HttpOnly cookie. draft-ietf-oauth-browser-based-apps describes it as the pattern that keeps every token out of the browser. A BFF can already sign in as a public client from the server (`client_id` and PKCE), and PR A of OI-171 gave the token endpoint a per-address budget of its own so one server can renew many people's tokens.

What a public BFF cannot do is protect its token store. Until this decision the code exchange authenticated nobody, and the refresh grant compared an optional `client_id`, which is no secret. A refresh token read out of a BFF's store (a backup, a log, a database read) therefore worked for whoever held it for the whole refresh lifetime, and an already-rotated one could be presented again to trigger the user-wide reuse cascade, which signs the person out of every application.

RFC 6749 §6 and the OAuth 2.0 Security Best Current Practice (RFC 9700, refresh token protection) bind a confidential client's refresh token to it by client authentication: a stolen token is useless without the client's credential. ADR 0002 already gave applications ES256 keys and a verified `private_key_jwt` assertion, for `client_credentials` only.

## Decision

### 1. Same application, a second redirect list

- One `client_id` (the application code), the same roles, permissions, access and access-token audience. A second exact-match list, **server-side redirect URIs** (`confidentialRedirectUris`), in its own table `ApplicationConfidentialRedirectUris` (byte-for-byte `Uri`, cascade on the application, unique per application).
- A code issued to a server-side redirect URI records `RequiresClientAuthentication`, and only a request authenticated with the application's sign-in key may redeem it. The existing list stays the public one.
- An address is in one list or the other (`Application.RedirectUriInBothLists`), checked on the lists as they stand after a save, so a request that sends one list is held against the stored other.
- Both sign-in lists (and the post-logout list) accept printable ASCII only, with no space (OI-166 (a)): authorize puts the address in a `Location` header, which refuses a control or non-ASCII character. Only a save checks it; a stored address the rule now refuses keeps working until a save resends it. The refusal is the list's code (`Application.RedirectUriInvalid`, `Application.ConfidentialRedirectUriInvalid` or `Application.PostLogoutRedirectUriInvalid`); a pointer to the offending entry comes only with two or more failures (`errors[]`, ADR 0001), and a single failure names the list, not the entry.
- A change to any of the three lists is audited with what it held before and after (`application.redirect_uris.changed`, OI-166 (c)). The audit handler logs its own failure and never throws.

### 2. One purpose per key

`ApplicationMachineKeys.Purpose`: 1 machine (`client_credentials`), 2 sign-in (`authorization_code` and `refresh_token`). Existing keys read 1. The cap of three active keys is per application **per purpose**, counted inside the insert under the same locks as before. A sign-in key sits on an internet-facing web server; refusing it at `client_credentials` keeps a leak of that server from becoming a machine credential, and refusing a machine key at the user grants keeps a batch job's key from redeeming a person's sign-in. ES256 only, as in ADR 0002.

The column has a named default and no CHECK constraint, as every column of that table: a CHECK's stored text is what schema compares report as drift. The API refuses any other value (`MachineKey.PurposeInvalid`).

### 3. One client authenticator

The assertion checks of ADR 0002 move into one service used by all three grants. In order: the assertion type; the key id; an active key of a live application; the key's purpose is the grant's; `client_id`, when sent, names the key's application; the signature and the assertion's rules (`aud` = the issuer, and nothing else); the `jti`, recorded once per application only after the signature verified, so an assertion accepted on one grant is refused on another. Every refusal is `Auth.InvalidClient`, with the reason in the log only. The refusal of a GUID-shaped application code, which guards the machine token's `sub`, stays in `client_credentials`: the user grants' `sub` is the user.

### 4. The rules at the token endpoint

- **CA1** A request carrying `client_assertion_type` or `client_assertion` is authenticated, with a sign-in key; a failure is `Auth.InvalidClient`. The assertion is never ignored.
- **CA2** Code exchange: the client is authenticated before anything is consumed. Who may consume a code is part of the one atomic consume statement: an unauthenticated request consumes public codes only, an authenticated one only its own application's codes. A refused request leaves the code to its rightful client.
- **CA2b** A redeemed server-side code presented again revokes what it issued only when the request is authenticated as its application. An unauthenticated replay answers `Auth.InvalidClient` and changes nothing: seeing a redeemed code is no way to end the server's session. (A bound refresh token can still be revoked by whoever holds it: §6.) As a backstop, a server-side code that a consume ever hands to an unauthenticated request is refused and issues nothing.
- **CA3** Refresh: the request is authenticated after the token lookup and before the `client_id` check, the replay grace, the revoked, family and reuse branches and any rotation. A bound token without the assertion is `Auth.InvalidClient` with nothing changed: not rotated, not counted as reuse, no notice. A first-party token with an assertion is `Auth.InvalidClient`. The check lives in the handler, so the first-party refresh endpoint meets it too.
- **CA4** `client_id` is optional when an assertion is present; when sent, it must name the key's application. An authenticated request counts as a confirmed client for the application replay grace.
- **CA5** A public code, or an unbound refresh token, presented with a valid assertion is accepted, and what it issues is bound: a public BFF can switch without signing anyone out. A public code redeemed this way is marked as the server's by the consume statement, so CA2b covers its replay too. A replay grace never hands a bound replacement to a request without the key.
- **CA6** PKCE S256 stays mandatory on every code.

### 5. Bound refresh tokens

`RefreshTokens.RequiresClientAuthentication` (and the same on `AuthorizationCodes`), nullable and appended last, so adding it changes metadata only. The flag is a parameter of the token's and the code's factory, so every path inherits it: the first issue, every rotation (bound when the token was bound or the request authenticated), the race sibling built from the rotation, and the grace replacement, which is a stored row. `sp_CreateRefreshToken` gains the parameter last, with a default, so the previous API's call keeps working.

### 6. Revocation

`POST auth/revoke` is unchanged: whoever holds a token may revoke it, and a `client_assertion` sent there is ignored. RFC 7009 §2.1 lets a server authenticate confidential clients at this endpoint; this one accepts the holder's revocation instead, deliberately, since revoking only takes access away. The consequence: someone who stole a bound refresh token cannot get tokens with it, but can end that person's session on the server by revoking it. Requiring the assertion to revoke a bound token is left to a later decision (OI-176). A server-side application's sign-out that also ends the browser's single sign-on session, with its key and its bound token, is [ADR 0004](0004-server-side-sign-out.md).

### 7. No caching

Every token response of the three grants carries `Cache-Control: no-store` and `Pragma: no-cache` (RFC 6749 §5.1). Before this decision only `client_credentials` did.

### 8. Discovery

Unchanged: it already lists `private_key_jwt` with ES256 and the three grants. RFC 8414 has no per-grant list.

## Consequences

- **The two intended behaviour changes.** (1) A public code or token sent with a client assertion that does not authenticate (a junk value, an expired assertion, another application's key) was answered as if the assertion were absent. It is now `Auth.InvalidClient`, and the code is not consumed. (2) The sign-in lists refuse, on save, an address with a control character, a space or a non-ASCII character. The console's edit dialog resends the public list on every save, so an application that already holds such an address cannot be saved until that address is fixed (percent-encoded) or removed.
- **Residual risk.** While the public list holds addresses, codes issued to them are redeemable without the key and their refresh tokens are unbound. Confidential mode protects only once the relying party has registered a sign-in key, moved its callback to the server-side list, and emptied the public list. The stricter address rule refuses only new saves. Whoever holds a bound refresh token can still revoke it (§6), which ends that person's session on the server, though it gets them no token.
- **Limits of the upgrade path (CA5).** Tokens issued before the switch stay unbound: a rotated one presented again without the key still meets today's reuse rule (the person is signed out everywhere). A request without the key that loses a rotation race to a request that bound the chain is refused (`Auth.InvalidClient`, nothing created). With `Jwt:RotateRefreshTokens` off, a refresh issues no new token, so an authenticated refresh binds nothing. The full guarantee comes from sign-ins on a server-side redirect URI.
- **Rollback of the database.** Nothing to undo: an older API ignores the new table and columns. It never reads the server-side list, so it refuses those addresses at authorize (fail closed).
- **Rollback of the API.** Before deploying an older API, revoke every sign-in key: an older API does not read `Purpose`, so it would accept a sign-in key at `client_credentials` as a machine key. When the console cannot do it, revoke them in SQL (`UPDATE [dbo].[ApplicationMachineKeys] SET [RevokedAt] = SYSUTCDATETIME(), [RevokeReason] = N'API rollback' WHERE [Purpose] = 2 AND [RevokedAt] IS NULL;`), and start the older API only once `SELECT COUNT(*) FROM [dbo].[ApplicationMachineKeys] WHERE [Purpose] = 2 AND [RevokedAt] IS NULL;` reads 0. Roll the console back with the API, or create no key while they differ: the newer console against an older API creates a machine key when Sign-in is chosen, and its server-side list is silently not saved. Under the older API, bound refresh tokens refresh without the key, and every token it rotates is written unbound; after the upgrade is deployed again, such a chain stays unbound until the server's next authenticated refresh rebinds it. Recreate the sign-in keys after the upgrade.

## Alternatives considered

- **One switch per application, "requires client authentication"** (as several identity providers do). Simpler, but turning it on breaks the browser application at once: there is no parallel run. Rejected (owner decision D-171-1).
- **A separate application for the server.** Its tokens would have another audience, and the people's roles and access are held by the first application, so each would have to be duplicated and kept in step. Rejected.
- **One key for every grant.** Simpler, and what some providers do. Rejected for least privilege (D-171-2).
- **RSA keys (PS256, RS256) now.** Every mainstream library signs ES256; one algorithm keeps the algorithm-confusion surface at zero. Deferred (D-171-3).
- **Reading the code before consuming it, then consuming it.** Correct as well, since the flag never changes, but it adds a read to every public exchange. The condition in the consume statement decides in one statement, and the public path's calls are unchanged.

## References

- RFC 6749 §2.3, §5.1, §6; RFC 7009 §2.1; RFC 7523 and draft-ietf-oauth-rfc7523bis; RFC 7636; RFC 9700 (OAuth 2.0 Security Best Current Practice); draft-ietf-oauth-browser-based-apps.
- [ADR 0002](0002-machine-clients.md): the key, the assertion rules and the jti store this decision reuses.
- Integration guide: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, section "Confidential mode: bind the tokens to your server's key".
