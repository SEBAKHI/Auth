# ADR 0006: The passkey RP ID is the registrable domain each deployment configures, never a constant

## Status

Accepted — 2026-11-05 (S20, PR A; decision D-T12 A). Behind `Passkeys:Enabled`, off by default. PR A stores, registers, lists and removes passkeys and counts them as a second factor; signing in with one is PR B, and PR C (2026-11-08) ships the pages and puts both settings in the console, which refuses an RP ID that does not cover the first-party origins and any new one while a live passkey is bound to another (§3 and §4 below, as rules on a console edit). Every passkey route answers 404 on a deployment until it switches `Passkeys:Enabled` on.

## Context

WebAuthn binds every passkey to an *RP ID*: a domain the browser checks against the page's origin. The RP ID must be the page's host or a registrable parent of it; a browser offers a passkey only on pages whose origin it covers, and never offers it under another RP ID.

The platform has two first-party applications on two hosts of one deployment, the console and the accounts app (`console.example.com` and `accounts.example.com`). A person who adds a passkey in the console must be able to sign in with it on the accounts app, so the RP ID cannot be the host of either one: it has to be the parent they share (`example.com`).

The value differs per deployment: `localhost` on a developer machine, `example.com` in the browser harness, each customer deployment its own domain. And it is permanent in a way most settings are not: a passkey made under one RP ID never works under another, so a value that changes strands every passkey made under the old one.

## Decision

### 1. A setting per deployment

The RP ID is `Passkeys:RpId`: the registrable domain the first-party apps share, or `localhost` on a developer machine. It is never a constant in the code, and never derived from `IdentityProvider:AccountsBaseUrl`, `IdentityProvider:PublicBaseUrl` or a request's `Host`.

### 2. Read once, stored with every passkey

The API reads it once at start-up, trimmed and lower-cased, after the database settings layer joined the configuration (PR C), so a value saved in the console is the one it starts with; a change applies after an application-pool recycle. Every passkey row stores the RP ID it was made under (`UserPasskeys.RpId`).

### 3. Validated before passkeys run

Passkeys run only when `Passkeys:Enabled` is on and all of these hold; otherwise every passkey route answers 404, and while the switch is on the security posture logs `passkey.configuration` at Critical with the reason:

- the RP ID is a host name: `localhost`, or two or more DNS labels; never a URL, an IP address or a port. It is the registrable domain itself, never a public suffix such as `co.uk`: browsers refuse a public suffix as an RP ID, so a value like that leaves nobody able to add a passkey;
- `IdentityProvider:FirstPartySpaOrigins` lists at least one origin, and each is a bare `https` origin whose host is the RP ID or ends with `.` and the RP ID (a plain suffix is not enough: `notexample.test` is not under `example.test`);
- the passkey tables are deployed;
- no live passkey is bound to another RP ID (section 4).

### 4. A new RP ID is refused while live passkeys carry another

When live passkeys carry an RP ID other than the configured one, passkeys stay off and the posture logs `passkey.rpid-mismatch` at Critical, whatever the switch says. An empty `Passkeys:RpId` is such a value too: no passkey counts as a factor under it, so the line is logged while any live passkey exists. There are two ways out:

- restore the previous value; or
- move on purpose: announce the move and ask people to add another way to sign in, switch `Passkeys:Enabled` off (a passkey added before the recycle would otherwise be bound to the old value, and passkeys would stay off after it), run `dotnet run --project Auth/Auth_Setup -- --retire-passkey-rp-id <old RP ID>` and execute the statement it prints against the database (one transaction; it revokes every live passkey of that RP ID and writes one audit row each, reason `rp-id-move`), then set `Passkeys:RpId` to the new domain, recycle the API and switch `Passkeys:Enabled` on again.

### 5. Only the RP ID in force counts

Only passkeys of the configured RP ID are listed, renamed, removed by their owner, used to sign in (PR B), or counted as a qualifying second factor. Revocations that protect an account act on every RP ID: a password reset or change, an administrator's lock (each for passkeys registered in the 72 hours before it), an administrator's two-factor reset, and the owner's break-glass script (every live passkey).

## Consequences

### Positive

- One passkey works in both first-party applications.
- A wrong value fails closed and loud: passkeys stay off, and a Critical posture line says why.
- A domain move is a deliberate operation with an audit row per retired passkey, not a silent loss.

### Negative

- A domain move retires every passkey: people sign in another way and add a passkey again under the new domain.
- An RP ID chosen too narrowly (one application's own host) cannot cover the other application; the origin check refuses it, but only once both origins are listed.
- Before PR C the two settings were edited in the settings file only. Since PR C they are in the console too (System settings › Passkeys), and a later change of `IdentityProvider:FirstPartySpaOrigins` that the RP ID does not cover is not refused there: the posture switches passkeys off until the two agree.

### Neutral

- Tests and documentation use `example.com`, the reserved `.test` names (RFC 6761) and `localhost` only.

## Alternatives Considered

### Alternative A: Derive the RP ID from a configured URL

Take the host of `AccountsBaseUrl` or `PublicBaseUrl`, or its parent. Rejected: the API's public host and the applications' parent need not be related, and an edit made for another reason (a new host for the API) would silently move every passkey.

### Alternative B: The accounts application's own host

Passkeys are added in the console as well as used on the accounts app; a passkey bound to one host is not offered on the other. Rejected.

### Alternative C: A constant in the code

The value differs per deployment and on a developer machine. Rejected.

### Alternative D: WebAuthn related origins (`/.well-known/webauthn`)

Lets one RP ID serve origins outside it. Not needed while both applications share a parent domain, and not available in every browser the platform supports. Not adopted; it remains an option if a deployment's applications ever stop sharing a parent.

## References

- W3C, *Web Authentication: An API for accessing Public Key Credentials, Level 3*: the RP ID (§4, Terminology) and its validation by the client (§5.1.3).
- `Auth/Auth.Application/Features/Passkeys/Common/PasskeyConfiguration.cs` (the rule), `PasskeyFeature.cs` (the refusal of section 4), `Auth/Auth.Infrastructure/Security/PasskeyPostureContributor.cs` (the posture lines), `Auth/Auth_Setup/PasskeyRpIdMoveScript.cs` (the move).
