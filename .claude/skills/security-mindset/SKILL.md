---
name: security-mindset
description: Load this skill when implementing any endpoint, authentication, authorization, data handling, secret or token storage, cookies, or cryptography. Apply security thinking to EVERY feature. Invoke when reviewing code for vulnerabilities, handling passwords/secrets, or when building anything that touches user data.
user-invocable: true
---

# Security Mindset

These checks apply to every stack. A "C#/.NET:" parenthetical names the .NET mechanism; other stacks use their equivalent.

## Apply to Every Feature

For every endpoint, component, service, and database operation, ask yourself:

| Question | What to Look For |
|----------|------------------|
| **"How would an attacker exploit this?"** | Think like a penetration tester. What inputs could be malicious? What assumptions can be violated? |
| **"What if this input is malicious?"** | SQL injection, XSS, command injection, path traversal. Never trust user input. Validation narrows input; parameterization and output encoding are the defenses. |
| **"What sensitive data could leak?"** | Check logs, error messages, API responses, stack traces. Are you exposing internal details? |
| **"What happens if this is called 10,000 times per second?"** | DoS potential. Rate limiting, resource exhaustion, database locks. |
| **"What if the user is authenticated but unauthorized?"** | Don't conflate authentication (who you are) with authorization (what you can do). |
| **"What if this fails partially?"** | Inconsistent state, orphaned records, leaked resources. Transactions, cleanup. |

## Security Code Review Checklist

### Input Validation
- [ ] All inputs validated server-side (client validation is for UX only)
- [ ] Input length limits enforced
- [ ] Input type checking implemented
- [ ] Allowlist validation preferred over denylist

C#/.NET mechanics: `/backend-development` → Validation Pipeline.

### Injection
- [ ] Queries are parameterized.
  - C#/.NET: EF Core LINQ, or `FromSql`/`ExecuteSql`/`SqlQuery` with interpolated values.
  - `FromSqlRaw`, `ExecuteSqlRaw` and `SqlQueryRaw` only with `DbParameter` values, and with identifiers chosen from a code-side allowlist.
- [ ] Output is encoded for its context (HTML body, attribute, JavaScript, URL). APIs return JSON with a JSON content type, never HTML built from input.
- [ ] External processes are started with an argument list and no shell (C#/.NET: `ProcessStartInfo.ArgumentList`, `UseShellExecute = false`), never with a concatenated command line.
- [ ] File paths are resolved to a full path and verified to stay under the permitted base directory. Client-supplied file names are never used as storage names.

### Authentication
- [ ] Passwords are hashed with Argon2id, the only approved algorithm for password and verifier-secret hashing (Cryptography Standards).
- [ ] Session tokens are cryptographically random, and session expiration is implemented.
- [ ] Password and credential-verification endpoints are throttled per account and per client, before the hash runs. A concurrency limit is sized so that concurrent Argon2id verifications fit in memory (C#/.NET: a partitioned rate-limiter policy plus a concurrency limiter on those endpoints).
- [ ] Repeated failures cause a temporary, escalating lockout per account. Failed attempts are monitored and alerted.
- [ ] Login, registration and password-reset responses are identical whether or not the account exists: same status, code and message, and comparable timing. For an unknown account, verify against a dummy Argon2id hash.
- [ ] Lockout reveals nothing: failed attempts against unknown identifiers are counted and locked like real accounts, and the locked response (status, code, message and timing) is the same for both.
- [ ] C#/.NET:
  - .NET ships no Argon2id, and ASP.NET Core Identity's default `PasswordHasher` uses PBKDF2, which is not approved.
  - Register an `IPasswordHasher<TUser>`, or the Application hashing port, implemented over a maintained Argon2id library.
  - Store the algorithm, parameters and salt with the hash (PHC string format), and verify in constant time.
- [ ] Hashes made with another algorithm, or with Argon2id below the current parameters, are upgraded at the next successful login (C#/.NET: `PasswordVerificationResult.SuccessRehashNeeded`).

### Secrets and Credential Storage

Three storage classes: verifier secrets are hashed; secrets presented to others and secrets the server reads back are encrypted or vaulted, never hashed.

- [ ] Verifier secrets — passwords, inbound API keys, refresh, reset and invitation tokens, and two-factor recovery codes (each single use) — are stored only as Argon2id hashes.
- [ ] High-entropy tokens use a `selector.verifier` format. The random selector is stored in clear and indexed for lookup, the verifier is stored as an Argon2id hash, and rate limiting applies before verification.
- [ ] Secrets presented to other parties are never hashed. These are outbound client secrets, third-party API keys, connection-string credentials and signing keys. They are held in a secrets manager or key vault, or supplied at deploy time. One that is stored in the application database is encrypted with AES-256-GCM under a key held outside that database.
- [ ] Secrets the server reads back to compute or verify a value are never hashed either. These are per-user TOTP seeds and the keys of HMACs or signatures that the application computes itself. Keys come from the secrets manager or key vault. A per-record secret stored in the application database, such as a TOTP seed, is encrypted with AES-256-GCM under a key held outside that database.
- [ ] Secrets never appear in source code, in configuration files committed to version control (C#/.NET: `appsettings*.json`), or in logs.
- [ ] Development uses a per-developer secret store (C#/.NET: user-secrets). Deployed environments use a secrets manager, a key vault or deploy-time environment values.

### Cryptography Standards

```
Password and verifier-secret hashing: Argon2id only
  (passwords, inbound API keys, refresh, reset and invitation tokens,
  two-factor recovery codes)
  Parameters (OWASP baseline): memory ≥ 19 MiB (19456 KiB), iterations ≥ 2,
  parallelism 1, salt 16 bytes from a CSPRNG, hash length 32 bytes.
  Not approved for these: bcrypt, scrypt, PBKDF2, the SHA-2 family.
Keyed hashing / pseudonymization of personal data (lookup keys, log correlation):
  HMAC-SHA-256 with a secret key from the secret store.
Integrity and fingerprints of non-secret data: SHA-256.
MD5 and SHA-1: never, for any security purpose.
Symmetric encryption: AES-256-GCM
  - 96-bit nonce, unique for every encryption under a key: from a CSPRNG
    (C#/.NET: RandomNumberGenerator.Fill) or a persisted counter; never a constant,
    a derived value, or a value taken from the plaintext.
  - 128-bit tag; the nonce and the tag are stored with the ciphertext.
  - The record's context (for example table, column and row id) is bound as
    associated data.
  - The key comes from a key vault or KMS, never from the same database,
    repository or configuration file; a key identifier is stored with each
    ciphertext so keys can rotate.
Asymmetric encryption / key transport: RSA-OAEP with SHA-256, keys ≥ 2048 bits
  (C#/.NET: RSAEncryptionPadding.OaepSHA256); never RSA PKCS#1 v1.5 encryption.
Key agreement: ECDH on P-256 or P-384 (C#/.NET: ECDiffieHellman).
Digital signatures: ECDSA P-256/P-384, or RSA ≥ 2048 bits. Ed25519 only through
  a maintained library while the platform has no built-in type (.NET has none).
JWT signing: RS256 or ES256.
```

This list governs algorithms that application code calls directly. Framework-managed protection keeps its vetted defaults and is not reconfigured to match it.
- C#/.NET: ASP.NET Core Data Protection (AES-256-CBC with HMAC-SHA256) protects auth cookies and antiforgery tokens.
- Its key ring is persisted to storage shared by every instance and is protected at rest.
- Data Protection is not used for long-term field encryption; that uses AES-256-GCM, as above.

### Cookies, Tokens and CSRF
- [ ] Browser clients keep the access token in memory only.
- [ ] The refresh token lives only in a cookie that the backend sets. The cookie is `HttpOnly` and `Secure`, sets `SameSite` explicitly, and scopes `Path` to the endpoints that read it. Delivery and rotation: `/backend-development` → Token Delivery.
- [ ] The refresh token is never in web storage. The one exception is a backend that cannot set cookies: the fallback that `/frontend-playbook` (rules/api-and-state.md) documents, recorded as open security debt.
- [ ] Every state-changing endpoint that the browser authenticates automatically (cookies, Basic, Windows) is CSRF-protected by one of these:
  - an antiforgery token;
  - for endpoints called only by script, a required custom request header plus a verified `Origin` against the CORS allowlist.

  A credential cookie sent cross-site (`SameSite=None`) needs both.
- [ ] Every credential cookie sets `SameSite` explicitly, and `SameSite` is never the only defense:
  - `Strict` for a cookie that only same-site requests read (the refresh cookie when frontend and API share a site);
  - `Lax` for a session cookie that must accompany top-level cross-site navigations (an authorization server's `authorize` endpoint); endpoints reached that way change no state on GET;
  - `None` only for a cookie that cross-site requests must carry, with both defenses above.
- [ ] Endpoints that accept only `Authorization`-header bearer tokens: verify that no cookie scheme is accepted on them, and record CSRF as N/A with that reason.
- [ ] TLS certificate validation is never disabled.
  - A private CA is trusted per client, by trusting that CA only.
  - A development certificate is trusted only in a Development-only registration that fails startup elsewhere (C#/.NET: `/backend-development` → Outbound HTTP).

### Authorization
- [ ] Access control checks on every request
- [ ] Authorization checked server-side, not client-side
- [ ] Principle of least privilege applied
- [ ] Resource ownership verified
- [ ] C#/.NET: where each check runs and what its denial returns: `/backend-development` → Authorization.

### Rate Limiting
- [ ] Every endpoint has a request-rate limit, partitioned per client: the authenticated user, otherwise the client's network address. Expensive endpoints get stricter limits, and credential endpoints also follow Authentication.
- [ ] A rejection returns 429 and the project's error contract, with `Retry-After` when the limiter knows the delay (C#/.NET mechanics: `/backend-development` → Error Pipeline).

### Data Protection
- [ ] Each data class has a recorded at-rest protection level, in the threat model or an ADR.
  - Storage-level encryption (database TDE, disk encryption) covers all persisted data and backups.
  - Application-level AES-256-GCM, with the key in a key vault or KMS, covers the classes that must stay confidential if the database or a backup is read.
  - A field encrypted at application level that must be looked up gets a separate HMAC-SHA-256 lookup column.
- [ ] Sensitive data encrypted in transit (TLS).
- [ ] PII handled according to regulations (GDPR, etc.).

### Logging Content
- [ ] Logs carry identifiers. They never carry secrets, credentials, tokens or personal data, email addresses included.
- [ ] Before the user is known, correlate with an HMAC-SHA-256 of the normalized value under a secret key.
- [ ] Request objects and event payloads are never logged.

C#/.NET mechanics: `/backend-development` → Logging.

### Error Disclosure
- [ ] Unexpected failures (5xx) return a generic body with no exception data. Details are logged server-side only.
- [ ] Expected failures (4xx) return the project's error contract with a stable machine-readable code (C#/.NET: `/backend-development` → Response Format). Their text never contains internal details.
- [ ] No stack traces in production responses.
