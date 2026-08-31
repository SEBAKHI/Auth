# Security Policy

This is a centralised identity provider. A defect here is not a defect in one
application; it is a defect in every application that trusts it. Please treat
findings accordingly, and give us the chance to fix one before it is public.

## Reporting a vulnerability

**Use GitHub's private vulnerability reporting.** Open the repository's
**Security** tab and choose **Report a vulnerability**. That channel is private
between you and the maintainers, needs no email address from either side, and
gives us a place to publish an advisory once a fix ships.

If private reporting is not enabled on this repository, or you cannot use it,
write to **info@sebakhi.com** instead. Say "security" in the subject line so it
is not read as a general enquiry.

**Please do not** open a public issue, a pull request, or a discussion for a
security defect. A public report is a public exploit for every deployment that
has not patched yet.

## What to include

The more of this you can give us, the faster the fix:

- The affected component — API, API Gateway, one of the SPAs, the SDK, or the
  database layer — and a file path or endpoint if you have one.
- A concrete path to the impact: what an attacker starts with, what they do, and
  what they end up holding. Three steps is usually enough.
- Version or commit hash you tested against, and whether you tested a local
  build or a deployed instance.
- Whether the finding requires an account, a particular configuration, or a
  particular network position.

Proof-of-concept code is welcome and never required.

## What to expect

| Stage | Target |
|---|---|
| Acknowledgement that a human has read your report | 3 working days |
| Initial assessment — severity, and whether we can reproduce it | 10 working days |
| Fix or a stated plan with a date | depends on severity; we will tell you which |

This project currently has a single active maintainer. Those targets are honest
intentions rather than a contractual SLA, and we would rather say so than
publish numbers we cannot hold.

## Disclosure

We ask for **90 days** from acknowledgement before public disclosure, or until a
fix is released, whichever comes first. If a finding is being actively exploited,
tell us — we will move faster and coordinate the timeline with you.

We will credit you in the advisory unless you ask us not to.

## Scope

**In scope** — anything that breaks one of these:

- Authentication: forging, replaying, or bypassing a token, session, or
  authorization code.
- Authorization: obtaining a permission you were not granted, including crossing
  an organization or application boundary.
- Tenant isolation: reading or writing another organization's data.
- Secret handling: extracting key material, or causing it to be stored or
  transmitted unprotected.
- Account takeover through any recovery, invitation, verification, or deletion
  flow.
- Injection, deserialization, or template-evaluation flaws in any endpoint.

**Out of scope:**

- Findings that require an already-compromised administrator account, unless the
  finding is that a specific control which is supposed to bound that compromise
  does not.
- Missing hardening headers or TLS configuration on a deployment we do not
  operate — configuration is the deployer's, and the deployment guide covers it.
- Rate-limiting behaviour observed against an instance reached by a path that
  bypasses the API Gateway. The gateway is the enforcement point by design.
- Automated scanner output with no demonstrated impact.
- Social engineering, physical access, and denial of service by raw volume.

## Operating this system safely

If you run a deployment of this project, two documents carry the controls that
are yours rather than ours:

- `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md` — secret storage modes, key backup,
  and the go-live checklist.
- `ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md` — the security internals,
  and the operational limits stated plainly rather than hidden.
