# ADR 0002: Machine clients authenticate with private_key_jwt and get a five-minute machine token

## Status

Accepted — 2026-10-11 (OI-128, PR A). Adds the `client_credentials` grant; changes no existing grant, endpoint or scheme. The endpoints that will accept the machine token (API-key validation, webhook-key validation, introspection) move onto it in a later change (OI-128, PR B).

## Context

An application's own server sometimes has to call this system as itself, with no user behind the call: to validate an API key one of its callers presented, to validate a webhook key, to introspect a token. Until this decision the only way the SDK could get a token for that was `LoginAsync(email, password, applicationId)`: a platform sign-in by a robot user whose password sits on the application's server. That design has four faults:

- A stored, reusable password on a server outside this system's control.
- A platform session, so the robot holds platform authority, not authority over its own application.
- Platform administrators must prove a second factor (S08). A robot cannot, so the day that switch is turned on every key validation of that application fails.
- Nothing binds the robot to the application it serves: any robot can validate any application's keys.

Mature identity providers authenticate service applications with OAuth 2.0 client credentials (RFC 6749 §4.4). Okta accepts only `private_key_jwt` for them; Microsoft Entra recommends certificate credentials over secrets. The same mechanism suits an application server that pulls data on a schedule.

## Decision

### 1. The credential: an application machine key

- An ES256 (ECDSA P-256) key pair. The server stores the **public** key only, as a JWK in table `ApplicationMachineKeys`, with its RFC 7638 thumbprint as `kid` (unique, so one public key belongs to one application).
- The console creates a key in one of two ways. The server generates the pair and returns the private key **once**, as PKCS#8 PEM, in the creation response (never stored, never logged), the way Okta's and Google's consoles hand out a generated service key. Or an administrator pastes the public half of a pair the team made; the private key then never leaves the team's machine. A pasted key must be `kty` `EC`, `crv` `P-256`, with `x` and `y` of a point on the curve, and no private member (`MachineKey.InvalidPublicKey` otherwise).
- At most three active keys per application (`MachineKey.LimitReached`), enough to replace a key by overlap. The count and the insert are one conditional statement.
- Managed under the API-key permissions (`apikeys:read`, `apikeys:create`, `apikeys:revoke`): a machine key opens only its own application's machine endpoints, so it is no stronger a credential than an API key. Soft-deleting an application revokes its machine keys with its API and webhook keys, in one transaction.

### 2. The grant: `client_credentials` with `private_key_jwt`

`POST /api/v1/auth/token` with `grant_type=client_credentials`, `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`, `client_assertion`, and optional `client_id` and `scope`. The assertion (RFC 7523 §3, draft-ietf-oauth-rfc7523bis) must have:

| Member | Rule |
|---|---|
| `alg` | Exactly `ES256`, pinned by IdentityModel's algorithm validation (`ValidAlgorithms`), the only alg check. `none`, every HMAC algorithm (including an HS256 MAC keyed with the public key), every RSA algorithm, and `ES384` or `ES512` under the same P-256 key are refused. |
| `kid` | Required; an active key of an active, not deleted application. |
| `typ` | Absent, `JWT` or `client-authentication+jwt`. |
| `iss`, `sub` | Both the application code; a form `client_id`, when sent, too. |
| `aud` | Exactly the issuer identifier (`Jwt:Issuer`, the discovery `issuer`), as the sole value. |
| `exp` | Required, not past, at most five minutes ahead (plus the clock skew). `nbf` and `iat`, when present, not in the future beyond the skew. |
| `jti` | Required, at most 200 characters, accepted once per application. |

- **Why the issuer as the audience, and only it.** The 2025 "audience injection" disclosure showed that an assertion whose audience is an endpoint URL, or a list, can be replayed at another server that shares an endpoint or a list member. rfc7523bis therefore requires the issuer identifier as the sole audience. RFC 7523 itself allowed the token endpoint; this system refuses it.
- **Single use.** The `jti` is recorded in `ClientAssertionJtis` by one conditional INSERT (`WHERE NOT EXISTS … WITH (UPDLOCK, HOLDLOCK)`), and only **after** the signature verified, so an anonymous caller cannot fill the table or pre-spend an application's identifiers. The daily sweep removes rows after the authorization codes' retention.
- **One answer for every refusal.** Every failed check answers `Auth.InvalidClient` (400); which check failed goes to the log only, so the answer is no oracle. An unknown scope, checked only after the client authenticated, answers `Auth.InvalidScope`.
- **Rate limiting.** The endpoint is anonymous; the binding is the signed assertion and its single-use `jti`. A server-to-server call has no browser, so no Origin check applies. The action's `login` policy and the gateway's `auth` policy cover it.
- **This grant only.** `private_key_jwt` authenticates `client_credentials` and nothing else: `authorization_code` and `refresh_token` stay public-client grants with PKCE, and ignore a `client_assertion` sent with them. Discovery's `token_endpoint_auth_methods_supported` describes the whole endpoint (RFC 8414 has no per-grant list), so it names both methods.
- **No GUID-shaped client.** Several readers take a GUID-shaped `sub` for a user id. The application code rule admits a GUID, so the grant refuses to mint for such a code (`Auth.InvalidClient`), and the machine scheme refuses such a `sub`.

### 3. The machine token

- RS256 under the same signing key as every access token; header `typ: at+jwt` (RFC 9068).
- Claims `iss`, `sub` = `client_id` = the application code, `aud` = `urn:authsystem:machine`, `scope` (one space-delimited string), `jti`, `iat`, `exp`. No user, session, role, permission, organization or authentication-method claim.
- Five minutes, a code constant, not a setting. No refresh token and no id token in the response.
- Machine scopes are a closed list in code: `apikeys.validate`, `webhookkeys.validate`, `tokens.introspect`. Absent `scope` grants all three. None is a permission code, and no permission check reads them.

### 4. The machine scheme

A third JwtBearer scheme, `MachineClient`, built from the same base parameters as the platform and userinfo schemes (issuer, key, RS256, lifetime), with `ValidAudience` the machine audience, `ValidTypes` `at+jwt`, and a token whose `client_id` is missing or differs from `sub`, or whose `sub` is GUID-shaped, refused. It runs only where an action names it; in this version none does.

- `urn:authsystem:machine` is not shaped like an application code (`:` is outside the code rule), so the userinfo scheme refuses a machine token by construction, and it is not `Jwt:Audience`, so the platform scheme refuses it too.
- A machine token passes `JwtBlacklistValidationMiddleware`: it has no `sid`, and its `sub` is not a user id.
- It is not revocable one by one: the revocation endpoint's validator keeps the platform and userinfo profiles only. Revoking the machine key stops new tokens at once; tokens already issued expire within five minutes.

### 5. Discovery

`grant_types_supported` lists `client_credentials`; `token_endpoint_auth_methods_supported` is `["none", "private_key_jwt"]`; `token_endpoint_auth_signing_alg_values_supported` is `["ES256"]`.

## Consequences

### Positive
- No stored password, no robot user, no platform authority: an application proves who it is with a key whose private half never reaches this system (or reaches it once, on generation).
- A stolen assertion is worth one token at most, and only here.
- S08's second-factor rule for platform administrators no longer stands in the way of server integrations, once PR B moves the machine endpoints onto this scheme.
- The same grant serves an application's scheduled data pull: a new scope, not a new mechanism.

### Negative
- A machine token cannot be revoked by itself; the window is five minutes.
- Whoever can sign with a machine key can also sign assertions for later (an `exp` five minutes after a moment of their choosing; `iat` and `nbf` are optional). Any compromise of the signing capability calls for revoking the key, even one where the private key itself could not be exported.
- A generated private key passes through this server once, in one response. A team that does not accept that pastes its own public key.
- `ClientAssertionJtis` grows with every token request until the daily sweep; at one request per application per four minutes (the SDK's cache) that is small.

### Neutral
- The SDK gains `ClientId`, `MachineKeyId` and `MachinePrivateKeyPem` options and `GetMachineAccessTokenAsync`; the machine token request goes through its own HTTP client, which never carries a user's bearer token.

## Alternatives Considered

### Alternative A: `client_secret_basic` (a shared secret per application)
Simple, and every OAuth library supports it. Refused: it is the stored-password problem again, one level down, and the secret travels on every token request. Okta does not allow it for service applications.

### Alternative B: mutual TLS (RFC 8705)
The strongest binding. Refused for now: every call reaches the API through the YARP gateway, which terminates TLS, so a client certificate would have to be verified at the gateway and forwarded to the API in a header the API trusts. Nothing in this system does that today, and building it is a larger change than the gap it would close.

### Alternative C: keep the robot user and exempt it from the second factor
No new mechanism. Refused: it keeps a stored password with platform authority, and an exemption from S08 is exactly the hole S08 closes.

## References
- RFC 6749 §4.4 (client credentials), RFC 7521 and RFC 7523 (JWT client authentication), draft-ietf-oauth-rfc7523bis (audience and `typ`), RFC 7638 (JWK thumbprint), RFC 9068 (JWT access tokens), RFC 8414 (authorization server metadata).
- `Auth/Auth.Application/Features/Authentication/ClientCredentials/ClientCredentialsCommandHandler.cs`, `Auth/Auth.Infrastructure/Authentication/ClientAssertionVerifier.cs`, `Auth/Auth_API/Common/Authentication/AccessTokenValidation.cs`.
