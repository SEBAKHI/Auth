# ROLE
You are a senior full-stack architect. Produce an **execution-ready implementation plan** (a plan — not code) for adding a legally-compliant **Account Deletion** capability to an existing project.

# STEP ZERO — MANDATORY REPOSITORY INSPECTION
Target: https://github.com/SEBAKHI/Auth (branch: `main`).
Before writing anything, inspect the repository's full structure: solution/projects, data-access approach, existing endpoints, migration strategy, frontend framework, and the exact inventory of installed shadcn/ui components.
Owner-declared facts to VERIFY (do not assume; if unverifiable, log in §13): backend follows the owner's standard — modular monolith, MediatR (CQRS), Dapper only (no Entity Framework), Argon2id hashing, YARP API Gateway as the sole public endpoint with the Auth API bound to loopback; frontend UI is built on shadcn/ui.
**Anti-hallucination rule:** never reference a file, endpoint, table, package, or component you have not verified in the repo, unless explicitly tagged `[TO BE CREATED]`.

# FIXED REQUIREMENTS (researched decisions — implement, do not re-litigate)
- **R1 — Two-phase deletion.** Immediate soft-deactivation (account hidden everywhere, login blocked) + 30-day grace period with recovery via re-authentication → then scheduled, irreversible hard deletion. A soft-delete flag is never the terminal state.
- **R2 — Data classification enforced in schema.** Class A = delete with account (profile, content, preferences). Class B = anonymize (analytics/telemetry: replace the user key with a random irreversible token). Class C = legal hold (financial/invoice records, security & fraud logs) isolated from profile tables, each class with a documented retention period.
- **R3 — Identity policy.** Internal immutable ID (GUID) fully decoupled from username/email; all foreign keys reference the immutable ID. Usernames and email addresses are **never recycled** after deletion (permanent reservation).
- **R4 — Tombstone registry.** On hard delete, write only `{hashed_identifier, deleted_at_utc, policy_version}` — zero PII. Destruction-operation logs retained ≥ 3 years.
- **R5 — Backups & crypto.** Backup expiry cycle ≤ 6 months; documented "re-apply deletion from tombstones" procedure after any restore; crypto-shredding (per-user key destruction) for encrypted personal data.
- **R6 — Compliance surfaces.** Prominent in-app "Delete account" + a standalone public web page to request deletion without the app; acknowledge and complete requests within ≤ 30 days (GDPR-compatible; CCPA 45-day compatible); a scheduled periodic-destruction job with interval ≤ 6 months (KVKK); retention disclosures linked to the privacy policy; if "Sign in with Apple" exists, revoke its tokens on deletion.
- **R7 — Deletion pipeline.** `AccountDeletionRequested` → re-authentication/verification → immediate deactivation + revocation of ALL sessions and tokens (access + refresh) → grace countdown → background worker executes staged deletion (cascade A, anonymize B, isolate C) → tombstone write → user notification. Reversible during grace, deterministic after it, idempotent, fully audited.

# BACKEND CONSTRAINTS
Zero unnecessary complexity: reuse the repo's existing architecture, patterns, naming, and data-access approach exactly as found; introduce no new framework, ORM, or layer; justify every new component in ≤ 1 line; follow the repo's existing error/response conventions; include DB migrations; unit-test coverage target 90% for new code.

# FRONTEND CONSTRAINTS
shadcn/ui **exclusively** — first inventory the components already installed in the repo, then map every UI element to the correct component; never hand-roll an equivalent of an available shadcn component. Destructive-action UX at professional grade: a "Danger Zone" `Card` in settings; `AlertDialog` with typed confirmation; a re-authentication `Dialog` step; clear grace-period messaging (`Alert` + `Badge` countdown state); `Form` with zod validation; `Button` loading/disabled states; `sonner` toasts; `Skeleton` loading; full keyboard/focus accessibility; responsive; RTL-safe layout via CSS logical properties.

# OUTPUT CONTRACT (strict)
Deliverable: a single Markdown file `ACCOUNT_DELETION_PLAN.md`, exported as a downloadable file. Language: English. If any Arabic text appears anywhere, wrap it in `<div dir="rtl">`. Use exactly this skeleton — tables are mandatory where stated, and no content outside it:
1. Scope & Non-Goals
2. Verified Current-State Summary (real files, paths, components found in Step Zero)
3. Architecture Map (components, dependencies, data flow, implementation order — ASCII diagram)
4. Top-5 Risk Table (Risk / Likelihood / Impact / Mitigation)
5. Data Model Changes (table: table / column / type / class A-B-C / purpose)
6. API Contract (table: method / route / auth / request / response / errors)
7. Backend Work Breakdown (phased tasks: ID / files touched / description / acceptance criteria)
8. Background Jobs & Scheduling (grace worker, staged deletion, periodic destruction ≤ 6 months)
9. Frontend Work Breakdown (table: screen / element / exact shadcn component / state & interaction)
10. Security & Failure Modes (edge cases incl.: deletion during an active session elsewhere, restore-after-delete, partial pipeline failure, re-registration attempt with a reserved identifier)
11. Test Matrix (unit / integration / E2E; coverage target)
12. Rollout & Migration Order (strict dependency order — no step may be skipped)
13. Assumptions & Open Questions + Definition of Done checklist

# REASONING PROTOCOL
Before drafting, silently map R1–R7 onto concrete repo locations; resolve conflicts with existing code or log them in §13. Output only the final plan — no reasoning narration, no preamble, no content outside the skeleton.

# ENVIRONMENT HOOKS (conditional)
If skills are available in this environment, consult before drafting: `implementation-strategy`, `backend-development`, `security-mindset`, `clean-architecture-structure`, `failure-mode-design`, `quality-assurance`, `final-review-checklist`.
