---
name: security-mindset
description: Load this skill when implementing any endpoint, authentication, authorization, data handling, or cryptography. Apply security thinking to EVERY feature. Invoke when reviewing code for vulnerabilities, handling passwords/secrets, or when building anything that touches user data.
user-invocable: true
---

# Security Mindset

## Apply to EVERY Feature

For every endpoint, component, service, and database operation, ask yourself:

| Question | What to Look For |
|----------|------------------|
| **"How would an attacker exploit this?"** | Think like a penetration tester. What inputs could be malicious? What assumptions can be violated? |
| **"What if this input is malicious?"** | SQL injection, XSS, command injection, path traversal. Never trust user input. |
| **"What sensitive data could leak?"** | Check logs, error messages, API responses, stack traces. Are you exposing internal details? |
| **"What happens if this is called 10,000 times per second?"** | DoS potential. Rate limiting, resource exhaustion, database locks. |
| **"What if the user is authenticated but unauthorized?"** | Don't conflate authentication (who you are) with authorization (what you can do). |
| **"What if this fails partially?"** | Inconsistent state, orphaned records, leaked resources. Transactions, cleanup. |

## Security Code Review Checklist

### Input Validation
- [ ] All inputs validated server-side (client validation is for UX only)
- [ ] Input length limits enforced
- [ ] Input type checking implemented
- [ ] Whitelist validation preferred over blacklist

### Authentication
- [ ] Passwords hashed with Argon2id (the ONLY approved hashing algorithm)
- [ ] API keys hashed with Argon2id before storage
- [ ] Session tokens are cryptographically random
- [ ] Session expiration implemented
- [ ] Secure cookie flags set (HttpOnly, Secure, SameSite)

### Cryptography Standards

```
┌─────────────────────────────────────────────────────────────┐
│              APPROVED CRYPTOGRAPHIC ALGORITHMS              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Password/Secret Hashing:                                   │
│  ✅ Argon2id (ONLY approved algorithm)                      │
│  ❌ bcrypt - NOT APPROVED                                   │
│  ❌ scrypt - NOT APPROVED                                   │
│  ❌ PBKDF2 - NOT APPROVED                                   │
│  ❌ SHA-256/SHA-512 - NOT APPROVED for passwords            │
│  ❌ MD5 - NEVER USE                                         │
│                                                             │
│  Argon2id Configuration (OWASP 2024 Baseline):              │
│  • Memory: 19 MiB minimum (19456 KB)                        │
│  • Iterations: 2 minimum                                    │
│  • Parallelism: 1                                           │
│  • Salt: 16 bytes, cryptographically random                 │
│  • Hash length: 32 bytes                                    │
│                                                             │
│  Symmetric Encryption: AES-256-GCM                          │
│  Asymmetric Encryption: RSA-2048+ or Ed25519                │
│  JWT Signing: RS256 or ES256                                │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Authorization
- [ ] Access control checks on every request
- [ ] Authorization checked server-side, not client-side
- [ ] Principle of least privilege applied
- [ ] Resource ownership verified

### Data Protection
- [ ] Sensitive data encrypted at rest
- [ ] Sensitive data encrypted in transit (TLS)
- [ ] PII handled according to regulations (GDPR, etc.)
- [ ] Secrets not hardcoded or logged

### Error Handling
- [ ] Detailed errors logged server-side only
- [ ] Generic errors shown to users
- [ ] No stack traces in production responses
- [ ] Failed attempts monitored and alerted
