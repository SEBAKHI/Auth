# ACCOUNT_DELETION_PLAN

> Revision 2 — incorporates owner decisions of 2026-07-27: Apple Sign-In in scope, per-user encryption at rest (crypto-shredding) in scope, admin `/permanent` behavior change approved, retention constants confirmed, cancellation email added as a fourth template.

## 1. Scope & Non-Goals

### Scope (Absolute MVP kernel)
- **Self-service, legally-compliant account deletion** for end users of the `accounts` app: re-authenticated request → immediate soft-deactivation + full credential revocation → 30-day grace with recovery via re-authentication → scheduled, irreversible, staged hard deletion (cascade Class A, anonymize Class B, isolate Class C) → zero-PII tombstone + permanent identifier reservation → completion notification (R1, R7).
- **Public, no-login deletion-request page** (`/delete-account` on the accounts SPA) with email-possession verification (R6).
- **Background execution**: grace worker + periodic retention/destruction sweep (≤ 6 months cadence; runs daily) + re-apply-after-restore mechanism (R5, R6).
- **Identifier policy enforcement**: registration paths reject permanently reserved (previously deleted) emails/usernames (R3).
- **Unification** *(owner-approved)*: the existing admin hard-delete purge (`DELETE /users/{id}/permanent`) is upgraded to the same staged destruction routine (tombstone + permanent reservation + anonymize-instead-of-delete logs).
- **Apple Sign-In** *(owner-added)*: full "Sign in with Apple" web integration via the existing `IExternalAuthProvider` strategy (login/register/recovery), server-side code exchange storing the Apple refresh token encrypted, and **Apple token revocation during the deletion pipeline** (R6).
- **Per-user encryption at rest + crypto-shredding** *(owner-added)*: per-user AES-256-GCM data-encryption keys (DEKs) wrapped by the existing DataProtection infrastructure; applied to `Users.PhoneNumber`, the TOTP secret (upgrading the existing app-level protector), and the Apple refresh token; DEK destruction during the deletion pipeline makes encrypted PII unrecoverable in every backup (R5).
- **Cancellation notification** *(owner-added)*: recovery during grace sends an `account-deletion-cancelled` email in addition to the audit row.

### Non-Goals
- Console (admin) UI changes — admin soft/hard delete UI already exists and is unchanged.
- GDPR data-export / portability (separate capability).
- Grace-period reminder emails (day-7 / day-1).
- SMS/Push channels for deletion notices (email only; matches current outbox usage).
- Encrypting identity/lookup columns (`Email`, `NormalizedEmail`, `Username`, names): they drive login lookups, unique constraints, search and display; their destruction remains physical deletion + reservation hashes. This is the documented boundary of the encryption scope.
- Admin-side restore UI for pending-deletion accounts (recovery is user re-authentication only).

---

## 2. Verified Current-State Summary

All items below were verified by direct inspection of the working copy of `SEBAKHI/Auth` (branch `main`).

### Backend — solution `Auth/Auth.sln`
| Area | Verified fact |
|---|---|
| Layout | Modular monolith: `Auth_API` (presentation, feature modules under `Auth_API/Modules/<Module>/Controllers`), `Auth.Application` (CQRS features under `Features/<Feature>/<UseCase>/`), `Auth.Domain` (entities, `Errors/*Errors.cs`, `Events/`, `Interfaces/Repositories/`), `Auth.Infrastructure` (Dapper repositories in `Persistence/`, auth services), `Auth.Shared`, `Auth.Sdk`, `Auth_Localization`, `API_Gateway` (YARP), `Auth_DB` (SSDT `.sqlproj` / DACPAC), `Auth_Setup` (console setup/ops tool), `Auth_API.Tests` |
| CQRS | MediatR commands/queries returning `ErrorOr<T>`; endpoints send via `ISender`; domain events via `IPublisher` + `INotificationHandler` (e.g. `UserDeletedAuditEventHandler` in `Auth_API/Modules/AuditLog/EventHandlers/`) |
| Data access | Dapper only (raw SQL + stored procedures via connection factory) — e.g. `Auth.Infrastructure/Persistence/UserRepository.cs`. No Entity Framework anywhere |
| Hashing | `Auth.Infrastructure/Authentication/Argon2PasswordHasher.cs` (passwords, OTPs); HMAC-SHA256 `ComputeTokenHash` convention for opaque tokens (refresh, password-reset, authorization codes) |
| Encryption at rest (exists) | `TwoFactorAuth.SecretKey` is already encrypted via ASP.NET DataProtection (`Auth.Infrastructure/Authentication/TwoFactorSecretProtector.cs` + tests) — **app-level key, not per-user**; DataProtection key ring + certificate wrapping and `DpapiSecretService`/`RsaKeyService`/`RefreshTokenKeyService` secret infrastructure verified in `Auth.Infrastructure/Security/` |
| Gateway | `API_Gateway/appsettings.json`: YARP deny-by-default per-feature route allowlist (`/api/v{version:int}/auth/…` with `auth` rate policy 20/min, `/api/v{version:int}/users/…` with `api` policy 100/min, …); `GatewayRouteCoverageTests.cs` enforces coverage; API reached only through the cluster address (loopback in production) |
| Admin deletion (exists) | `DELETE /api/v1/users/{id}` (`users:delete`) = soft delete via `DeleteUserCommandHandler` (blocks when an owned org has other members; deletes sole-member owned orgs); `DELETE /api/v1/users/{id}/permanent` (`users:manage`) = `HardDeleteUserCommandHandler` → `UserRepository.HardDeleteAsync` with UPDLOCK/HOLDLOCK eligibility re-check and a ~20-table purge/reattribution transaction guarded by `UserHardDeleteSqlTests`; `includeDeleted` listing gated by `users:manage` |
| Users schema | `Auth_DB/dbo/Tables/Core/Users.sql`: immutable GUID `Id` PK; `IsDeleted`/`DeletedAt`/`DeletedBy`; table-wide `UNIQUE` on `Username` and `NormalizedEmail`; `Status` 1–4; `SecurityStamp`; `PhoneNumber NVARCHAR(20) NULL` (currently plaintext, and a sortable column in `SortFields.Users`) |
| Login | `sp_ValidateCredentials` filters `IsDeleted = 1` rows entirely (a deleted account behaves as "not found"); Argon2id verification in C#; lockout + 2FA challenge flows exist |
| Sessions/tokens | `UserSessions`, `RefreshTokens` (+ `sp_RevokeAllUserTokens`, `sp_RevokeRefreshToken`), `IdpSessions` (`IdpSessionRepository`), `AuthorizationCodes`, `RevokedTokens` + in-memory `TokenBlacklistService` persisted by `TokenRevocationBackgroundService`; `TerminateAllSessionsCommandHandler` terminates sessions, revokes their refresh tokens, and blacklists session ids |
| Background workers | `BackgroundService` pattern registered in `Auth_API/Program.cs` (`TokenRevocationBackgroundService`, `NotificationTemplateStartupCheck`, `NotificationOutboxDispatcher`). Outbox dispatcher: signal + poll fallback, startup reclaim of orphaned `Processing` rows, at-least-once |
| Notifications | DB-backed templates: `NotificationTypes` (seed `SeedData/10_NotificationTypes.sql`, GUID ids `40000000-…`), `NotificationTemplates`/`Versions`/`Translations`, `NotificationOutbox` (`Recipient` string + loose `RecipientUserId`, content rendered at enqueue), Upgrades scripts pattern `dbo/Scripts/Upgrades/YYYY-MM-DD_*.sql` |
| Verification OTPs | `EmailVerificationTokens` (Argon2id `OtpHash`, `AttemptCount` max 5, expiry, rate-limit index by email) |
| Audit | `AuditLogs` (loose `UserId`/`PerformedBy`, JSON old/new values, no FK); `[Event]AuditEventHandler` convention; current purge **deletes** the user's audit rows and reattributes actor refs to `WellKnownUserIds.System` |
| External auth | Strategy pattern in place and **pre-designed for Apple**: `IExternalAuthProvider` ("Each provider (Google, Apple, Facebook…)") + `ExternalAuthProviderFactory` + `GoogleAuthProvider` (id-token validation); `ExternalAuthProviders` table (`Code` comment enumerates `'apple'`) with table-driven `GET /auth/external-providers`; `UserExternalLogins` (`Provider` comment enumerates `'apple'`; **no refresh-token column today**); `ExternalLoginRequest { Provider, IdToken, Nonce?, CreateOrganization }` — **no authorization-code or name fields today**; only Google is seeded (`SeedData/09_ExternalAuthProviders.sql`); `PasswordHash` nullable (passwordless accounts exist) |
| Errors | `Auth.Domain/Errors/UserErrors.cs` et al.; codes `User.<Name>`; existing: `CannotDeleteSystemUser`, `CannotDeleteOrganizationOwner`, `CannotDeletePersonalOrganizationWithMembers`, `NotSoftDeleted`, `DuplicateEmail`, `InvalidCredentials` |
| Migrations | DACPAC publish + idempotent seed scripts (post-deployment) + dated `Upgrades/` scripts for prod; `Auth_Setup` console exists for operational one-shots |

### Frontend — `Auth_UI/` (pnpm workspace)
| Area | Verified fact |
|---|---|
| Workspace | `apps/accounts` (end-user), `apps/console` (admin), `packages/{account,api,auth,i18n,ui}`, `e2e/` (Playwright); **`Auth_UI/components.json` present** — components are managed via the shadcn CLI (run from `Auth_UI/`, pnpm runner) |
| shadcn/ui inventory (`packages/ui/src/`) | `alert-dialog`, `avatar`, `badge`, `breadcrumb`, `button`, `calendar`, `card`, `chart`, `checkbox`, `collapsible`, `command`, `dialog`, `dropdown-menu`, `field`, `form`, `input`, `input-group`, `input-otp`, `label`, `menubar`, `native-select`, `pagination`, `popover`, `radio-group`, `scroll-area`, `select`, `separator`, `sheet`, `sidebar`, `skeleton`, `sonner`, `switch`, `table`, `tabs`, `textarea`, `tooltip` + `common/` (incl. `confirm-dialog`, `form-dialog`, `page-header`) + `hooks/use-countdown.ts`. **`alert` is NOT installed** `[TO BE CREATED]` |
| accounts routes (`apps/accounts/src/routes.tsx`) | Anonymous: `/login`, `/register`, `/forgot-password`, `/reset-password`; authed shell: `/profile`, `/organizations…`; top-level dual-state: `/two-factor`, `/verify-email`, `/accept-invitation` |
| External sign-in UI | `apps/accounts/src/components/google-sign-in.tsx`: singleton external-script loader, provider button, nonce, theme/locale awareness — the pattern the Apple button mirrors |
| Profile page (`packages/account/src/pages/profile/profile-page.tsx`) | `Tabs` (Account / Security / Sessions), `Card` sections, `react-hook-form` + `zod` + `Form`/`FieldGroup`, `sonner` toasts, `Skeleton`, `@astoom/api` client |
| i18n | 7 type-enforced locale files (`en, ar, tr, fr, zh, ur, fa`) with parity test |

### Owner-declared facts — verification result
Modular monolith ✅ · MediatR CQRS ✅ · Dapper only ✅ · Argon2id ✅ · YARP sole public endpoint ✅ · shadcn/ui frontend ✅. No deviations found.

---

## 3. Architecture Map

```
┌─ TOPOLOGY (spatial) ────────────────────────────────────────────────────────────
│
│  accounts SPA (apps/accounts)                    API_Gateway (YARP allowlist)
│  ├─ /profile → Danger Zone ────────────────┐     /api/v1/users/*  (api policy)
│  ├─ /delete-account (public, anon) ────────┼───► /api/v1/auth/*   (auth policy)
│  ├─ /account-recovery (grace) ─────────────┘          │  (loopback only)
│  └─ login/register: google-sign-in + apple-sign-in    ▼
│
│  Auth_API (presentation)
│  ├─ UsersController   POST /users/me/deletion, /users/me/deletion/send-code
│  └─ AuthController    POST /auth/deletion/{request|confirm|recover|recover-external}
│                       login / external-login / register (modified)
│           │ ISender
│           ▼
│  Auth.Application/Features/AccountDeletion (new)
│  ├─ RequestAccountDeletion ─┐    ┌─ RecoverAccount / RecoverAccountExternal
│  ├─ SendDeletionReauthCode  ├───►│  ExecuteAccountDeletion (worker-invoked)
│  ├─ PublicRequestDeletion   │    └─ shared: OwnedOrganizationDeletionGuard,
│  └─ ConfirmPublicDeletion ──┘         ICredentialRevocationService,
│           │                           IPerUserCryptoService (interface)
│           │ IPublisher (events) → AuditEventHandlers + NotificationEventHandlers
│           ▼
│  Auth.Domain: AccountDeletionRequest, AccountDeletionTombstone (entities),
│               AccountDeletionStatus/Source (enums), UserErrors additions,
│               AccountDeletion{Requested|Cancelled|Completed}Event records,
│               repository interfaces (requests, tombstones, verifications, keys)
│           ▲ implemented by
│  Auth.Infrastructure
│  ├─ Persistence: AccountDeletionRequestRepository, TombstoneRepository,
│  │               DeletionVerificationRepository, UserEncryptionKeyRepository,
│  │               UserRepository (unified staged destruction)
│  ├─ Authentication: AppleAuthProvider (strategy, mirrors GoogleAuthProvider),
│  │                  AppleClientSecretGenerator (ES256 .p8 via SecretManagement),
│  │                  AppleTokenRevocationService (IExternalTokenLifecycle),
│  │                  TwoFactorSecretProtector v2 (per-user DEK layer)
│  ├─ Security: PerUserCryptoService (AES-256-GCM DEK, DataProtection-wrapped)
│  └─ AccountDeletion/AccountDeletionWorker : BackgroundService
│               (grace executor + daily retention sweep)
│           │ Dapper (transactions)
│           ▼
│  Auth_DB: AccountDeletionRequests, AccountDeletionTombstones,
│           AccountDeletionVerifications, UserEncryptionKeys;
│           UserExternalLogins +ProviderRefreshTokenEnc; Users.PhoneNumber widened;
│           apple provider seed (disabled); 4 notification types + templates;
│           Upgrades script
│
│  Dependencies flow inward only: API → Application → Domain; Infrastructure →
│  Application/Domain. No new framework, ORM, or layer. Stack unchanged
│  (.NET + Dapper + MediatR + ErrorOr + YARP + SSDT | React + shadcn/ui).
│  External integration added: Apple ID endpoints (JWKS, /auth/token, /auth/revoke).
└─────────────────────────────────────────────────────────────────────────────────

DATA FLOW PIPELINE (R7, step-by-step, deterministic):

 [A] In-app request                      [B] Public no-login request
 user (bearer) → Danger Zone            email → POST /auth/deletion/request
 → re-auth (password | OTP)             → (account exists?) enqueue OTP email → 202 always
 → typed AlertDialog confirm            → POST /auth/deletion/confirm {email, otp}
 → POST /users/me/deletion              → Argon2id verify + attempt cap
        │                                        │
        └───────────────┬────────────────────────┘
                        ▼
 (1) Guards: system-user; owned-org rule; no active request (filtered unique idx)
 (2) INSERT AccountDeletionRequests (PendingGrace, GraceEndsAtUtc = now + 30d)
 (3) Soft-deactivate: Users.IsDeleted = 1  → hidden everywhere, login blocked
 (4) Revoke ALL: UserSessions terminated + session-ids blacklisted + RefreshTokens
     revoked + IdpSessions deleted (ICredentialRevocationService)
 (5) AccountDeletionRequestedEvent → audit row + "account-deletion-requested" email
                        │
              ┌─────────┴──────────┐
              ▼ (grace, ≤30d)      ▼ (GraceEndsAtUtc reached)
 login/external-login with     AccountDeletionWorker poll (15 min + startup catch-up)
 valid creds → 403             → claim: Status PendingGrace→Processing (optimistic)
 Auth.AccountPendingDeletion   → ExecuteAccountDeletionCommand (idempotent stages):
 {graceEndsAtUtc}                 (a) re-verify IsDeleted=1 (UPDLOCK)
 → /account-recovery screen       (b) MERGE tombstone {EmailHash, UsernameHash,
 → POST /auth/deletion/recover        DeletedAtUtc, PolicyVersion}  [zero PII]
 → Status→Cancelled,              (c) snapshot email/name for final notice
   IsDeleted=0, auto-login        (d) revoke Apple tokens via /auth/revoke
   (LoginResponseBuilder)             (decrypts stored refresh token — needs DEK;
   + AccountDeletionCancelled-        retryable, non-blocking after attempt cap)
   Event → audit row +            (e) CRYPTO-SHRED: delete UserEncryptionKeys row
   "account-deletion-cancelled"       → PhoneNumber/TOTP/Apple-token ciphertexts
   email                              unrecoverable in DB AND all backups (R5)
                                  (f) Class B/C: anonymize AuditLogs + LoginAttempts
                                  (g) Class A: unified cascade purge (existing list)
                                      + reattribute actor refs → System + Users row
                                  (h) delete profile-image file (post-commit)
                                  (i) Status→Completed; AccountDeletionCompletedEvent
                                      → destruction audit + completion email (snapshot)
                                  failure → AttemptCount++, retry; Dead after 5

 DAILY SWEEP (same worker): re-apply deletions for restored users (Completed request
 + live Users row); purge expired AccountDeletionVerifications; purge LoginAttempts
 > 365d; purge Sent NotificationOutbox > 180d.  (KVKK ≤ 6-month cadence: daily.)

IMPLEMENTATION ORDER: DB → Domain → shared refactors (guard/revocation/unified purge)
→ per-user crypto foundation (+ migration) → Application commands → API endpoints +
event handlers → worker → Apple provider → frontend → E2E.
```

---

## 4. Top-5 Risk Table

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Unifying the purge (tombstone + anonymize-instead-of-delete for logs) regresses the existing admin `/permanent` flow or misses a table on schema drift | Medium | High | Single shared `UserRepository.HardDeleteAsync` routine for both flows; extend `UserHardDeleteSqlTests` FK/behavior guard to the new tables and to the anonymization asserts; phase P2 lands and goes green before any new endpoint ships |
| 2 | Worker never fires on idle IIS app pool → deletions execute late; >30-day completion breaches R6 | Medium | High | Same constraint already accepted for the notification outbox (verified precedent): app-pool preload/always-running required; startup catch-up drains overdue requests on first request-triggered warmup; worker logs an ERROR when any request is >24h overdue (alarm surface in Serilog) |
| 3 | Loss of DataProtection master material (certificate/key ring) — which wraps every per-user DEK, the TOTP protector, and the identifier-HMAC key — bricks encrypted fields and reservation checks (an accidental global crypto-shred) | Low | Critical | Reuse the already-operational certificate-backed DataProtection setup; certificate + key-ring backup is an existing prod runbook item — extend it to name the new dependents; versioned ciphertext prefixes allow staged key migration; reservation matching is additionally backed by retained `AccountDeletionRequests.UserId` |
| 4 | Encryption migration (TOTP secrets to per-user DEK, phone numbers to ciphertext) corrupts data → every 2FA user locked out | Low | High | Versioned payload prefixes with dual-read (v1 app-level, v2 per-user) — old rows stay readable forever; idempotent `Auth_Setup` migration command; migrate-verify-count report; staging rehearsal mandatory before prod (§12) |
| 5 | Partial pipeline failure (crash mid-purge; Apple `/auth/revoke` outage; file-delete or email enqueue failure after commit) leaves inconsistent state | Medium | High | All DB stages in one transaction with idempotent re-runnable design (MERGE tombstone, WHERE-guarded deletes); Apple revocation retried across execution attempts and, after the cap, deletion proceeds anyway (Apple tokens expire naturally; failure recorded in the destruction audit detail); post-commit steps retried by re-claiming; `Processing` rows reclaimed at startup; after 5 attempts → `Failed` + ERROR alarm |

---

## 5. Data Model Changes

New objects live in `Auth_DB/dbo/Tables/Security/` (+ named defaults per DACPAC house rules). Schema ships to every environment via the regular DACPAC publish diff (all changes are additive — no data-loss operations); `dbo/Scripts/Upgrades/` stays reserved for data reconciliation, per house convention. Seeds run automatically through the post-deployment script. Class legend: **A** delete with account · **A-E** = Class A stored encrypted with the per-user DEK (crypto-shredded before cascade) · **B** anonymize · **C** legal hold / destruction evidence.

### New table: `AccountDeletionRequests` `[TO BE CREATED]` — Class C (destruction log, retained ≥ 3 years — confirmed)
| Table | Column | Type | Class | Purpose |
|---|---|---|---|---|
| AccountDeletionRequests | Id | UNIQUEIDENTIFIER PK, DF NEWID() | C | Request identity |
| AccountDeletionRequests | UserId | UNIQUEIDENTIFIER NOT NULL (loose ref, **no FK** — precedent: `NotificationOutbox.RecipientUserId`) | C | Immutable subject id; survives user purge; drives restore re-apply |
| AccountDeletionRequests | Status | TINYINT NOT NULL, CK IN (1..5) | C | 1 PendingGrace, 2 Cancelled, 3 Processing, 4 Completed, 5 Failed |
| AccountDeletionRequests | Source | TINYINT NOT NULL, CK IN (1,2) | C | 1 InApp, 2 PublicWeb |
| AccountDeletionRequests | RequestedAtUtc / GraceEndsAtUtc | DATETIME2 NOT NULL | C | Grace window 30d — confirmed (R1); acknowledgment timestamp (R6) |
| AccountDeletionRequests | CancelledAtUtc / CompletedAtUtc | DATETIME2 NULL | C | Terminal timestamps |
| AccountDeletionRequests | PolicyVersion | NVARCHAR(20) NOT NULL | C | Format `YYYY.MM` — confirmed; initial value `2026.07` (R4) |
| AccountDeletionRequests | AttemptCount | INT NOT NULL DF 0 | C | Execution retries |
| AccountDeletionRequests | LastError | NVARCHAR(2000) NULL | C | Diagnostics (no PII; records Apple-revocation failure if any) |
| AccountDeletionRequests | CreatedAt / CreatedBy | house audit pattern | C | Attribution |
| AccountDeletionRequests | *(index)* | UNIQUE filtered `(UserId) WHERE Status IN (1,3)` | — | Exactly one active request per user; `(Status, GraceEndsAtUtc)` for worker scan |

### New table: `AccountDeletionTombstones` `[TO BE CREATED]` — Class C (permanent, zero PII — R4)
| Table | Column | Type | Class | Purpose |
|---|---|---|---|---|
| AccountDeletionTombstones | Id | UNIQUEIDENTIFIER PK | C | — |
| AccountDeletionTombstones | EmailHash | NVARCHAR(200) NOT NULL, UNIQUE | C | HMAC-SHA256(NormalizedEmail) — permanent reservation (R3) + restore matching (R5) |
| AccountDeletionTombstones | UsernameHash | NVARCHAR(200) NOT NULL, index | C | HMAC-SHA256(UPPER(Username)) — username never recycled (R3) |
| AccountDeletionTombstones | DeletedAtUtc | DATETIME2 NOT NULL | C | Destruction instant |
| AccountDeletionTombstones | PolicyVersion | NVARCHAR(20) NOT NULL | C | R4 contract: {hashed_identifier, deleted_at_utc, policy_version} — nothing else |

### New table: `AccountDeletionVerifications` `[TO BE CREATED]` — Class A (short-lived; purged by sweep)
| Table | Column | Type | Class | Purpose |
|---|---|---|---|---|
| AccountDeletionVerifications | Id | UNIQUEIDENTIFIER PK | A | — |
| AccountDeletionVerifications | UserId | UNIQUEIDENTIFIER NULL (loose) | A | Subject (when known) |
| AccountDeletionVerifications | Email | NVARCHAR(255) NOT NULL | A | OTP destination; rate-limit key (mirrors `EmailVerificationTokens`) |
| AccountDeletionVerifications | OtpHash | NVARCHAR(500) NOT NULL | A | Argon2id hash of 6-digit code (house rule: Argon2id only) |
| AccountDeletionVerifications | ExpiresAt / UsedAt / AttemptCount / CreatedAt | as in `EmailVerificationTokens` | A | Expiry 15 min; max 5 attempts |

### New table: `UserEncryptionKeys` `[TO BE CREATED]` — key material (crypto-shredded at destruction, R5)
| Table | Column | Type | Class | Purpose |
|---|---|---|---|---|
| UserEncryptionKeys | UserId | UNIQUEIDENTIFIER PK, FK → Users | — | One DEK per user, created lazily on first encrypted write |
| UserEncryptionKeys | WrappedDek | NVARCHAR(2000) NOT NULL | — | 32-byte AES-256-GCM DEK wrapped by DataProtection (purpose `"UserDek"`; precedent: `TwoFactorSecretProtector`) |
| UserEncryptionKeys | KeyVersion | INT NOT NULL, DF 1 | — | Future master-rotation support |
| UserEncryptionKeys | Algorithm | NVARCHAR(20) NOT NULL, DF N'AES-256-GCM' | — | Approved algorithm (house crypto standard) |
| UserEncryptionKeys | CreatedAt | DATETIME2 NOT NULL, DF GETUTCDATE() | — | — |

### Altered tables
| Table | Column | Type | Class | Purpose |
|---|---|---|---|---|
| UserExternalLogins | ProviderRefreshTokenEnc `[TO BE CREATED]` | NVARCHAR(2000) NULL | A-E | Apple refresh token (obtained by server-side code exchange), AES-256-GCM under the user DEK with field purpose as AAD; decrypted once — during pipeline stage (d) revocation |
| Users | PhoneNumber | NVARCHAR(20) → **NVARCHAR(500)** | A-E | Becomes versioned ciphertext (`v2:` prefix) under the user DEK; plaintext legacy values (no prefix) readable until migration completes. **Consequence (needs owner ack, §13): phone sort/search leaves the `SortFields.Users` allowlist** |
| TwoFactorAuth | SecretKey | NVARCHAR(500) (unchanged) | A-E | Protector upgraded to v2: per-user DEK inside the existing DataProtection envelope; versioned prefix keeps v1 rows readable; one-time `Auth_Setup` re-encryption migration |
| ExternalAuthProviders | *(seed row)* | `('apple', 'Apple', icon, IsEnabled = 0, order 2)` | — | Seeded **disabled**; enabled in §12 only after Apple credentials are provisioned (table-driven provider list picks it up automatically) |

### Existing tables — classification enforced by the unified destruction routine
| Table | Column | Type | Class | Purpose / action at destruction |
|---|---|---|---|---|
| Users | *(row)* | — | A | Deleted last, `WHERE IsDeleted = 1` guard (existing) |
| RefreshTokens, UserSessions, IdpSessions, AuthorizationCodes, UserExternalLogins, EmailVerificationTokens, PasswordResetTokens, PasswordHistory, TwoFactorChallenges, TwoFactorAuth, RevokedTokens, UserRoles, UserPermissions, OrganizationUsers, OrganizationUserRoles, OrganizationUserPermissions, OrganizationInvitations (authored), OwnershipTransferCodes, NotificationOutbox (recipient rows), sole-owned Organizations, AccountDeletionVerifications, **UserEncryptionKeys (stage e — before every other stage that would still need the DEK has run)** | *(rows)* | — | A | Cascade delete (existing purge list + the new tables); profile-image file under `/uploads` also deleted `[behavior TO BE CREATED]` |
| AuditLogs | UserId, PerformedBy, OldValues, NewValues, Details, IpAddress | — | B→C | **Changed from delete to anonymize** *(owner-approved)*: `UserId→NULL`, `PerformedBy→System`, PII payload columns → NULL; `Action`/`EntityType`/`Timestamp` retained (security log, legal hold; destruction-op rows retained ≥ 3 years — confirmed, per R4) |
| LoginAttempts | UserId, Username | — | B→C | **Changed from delete to anonymize** *(owner-approved)*: `UserId→NULL`, `Username→N'[deleted]'`; IP retained for fraud analysis within 365-day retention — confirmed, then purged by sweep |
| OrganizationApplications.EnabledBy, OrganizationUsers.InvitedBy, OrganizationUserRoles.AssignedBy, OrganizationUserPermissions.GrantedBy, OrganizationInvitations.AcceptedByUserId | actor refs | — | B | Reattributed to `WellKnownUserIds.System` / NULL (existing behavior, unchanged) |

No financial/invoice tables exist (verified) — the Class C "financial records" branch of R2 is vacuously satisfied (owner-acknowledged).

---

## 6. API Contract

All endpoints ride existing gateway prefixes (`auth-route`, `users-route`) — **no gateway config change**; `GatewayRouteCoverageTests` passes unchanged. Error/response envelope follows the repo's existing `ErrorOr` → `Problem(errors)` convention.

| Method | Route | Auth | Request | Response | Errors |
|---|---|---|---|---|---|
| POST | `/api/v1/users/me/deletion` | Bearer (self) | `{ password?, otpCode? }` (password accounts: `password`; passwordless: `otpCode` from send-code) | `202 { graceEndsAtUtc }` | 400 validation; 401; 403 `User.CannotDeleteSystemUser`, `User.InvalidCurrentPassword`; 400 invalid OTP; 409 `User.DeletionAlreadyRequested`, `User.CannotDeleteOrganizationOwner`, `User.CannotDeletePersonalOrganizationWithMembers` |
| POST | `/api/v1/users/me/deletion/send-code` | Bearer (self) | `{}` | `202` (always) | 401; 429 via attempt/rate caps |
| POST | `/api/v1/auth/deletion/request` | Anonymous | `{ email }` | `202` (always — anti-enumeration; OTP email sent only if the account exists) | 400 validation only |
| POST | `/api/v1/auth/deletion/confirm` | Anonymous | `{ email, otpCode }` | `202` (also when a request is already pending — idempotent) | 400 `AccountDeletion.InvalidOtp` (identical for unknown email / wrong code / expired); 409 owned-org conflicts (as above) |
| POST | `/api/v1/auth/deletion/recover` | Anonymous | `{ email, password, twoFactorCode? }` | `200 LoginResponse` (auto-login via `LoginResponseBuilder`; request → Cancelled, `IsDeleted→0`; **cancellation email enqueued** — confirmed) | 400 validation; 401 `User.InvalidCredentials` (also when no pending request — no state leak); 403 `User.AccountLocked`, `User.TwoFactorRequired`, `User.RecoveryWindowExpired` |
| POST | `/api/v1/auth/deletion/recover-external` | Anonymous | `{ provider, idToken }` (`google` or `apple`) | `200 LoginResponse` (same cancellation side effects) | 400/401 provider validation; 403 `User.RecoveryWindowExpired` |
| POST | `/api/v1/auth/login` *(modified)* | Anonymous | unchanged | unchanged + on valid credentials for a pending-deletion account: 403 `Auth.AccountPendingDeletion` with `graceEndsAtUtc` | existing errors unchanged |
| POST | `/api/v1/auth/external-login` *(modified)* | Anonymous | `ExternalLoginRequest` + new optional fields: `AuthorizationCode?` (Apple: required — server exchanges it at `appleid.apple.com/auth/token` and stores the refresh token encrypted), `GivenName?`/`FamilyName?` (Apple sends the name only on first authorization, client-side; used solely at first registration, sanitized) | unchanged + pending-deletion signal as login | existing + `ExternalAuth.*` validation errors |
| GET | `/api/v1/auth/external-providers` *(unchanged code)* | Anonymous | — | now also returns `apple` once the seeded row is enabled (table-driven — verified) | — |
| POST | `/api/v1/auth/register` + invitation register + admin `POST /users` *(modified)* | as today | unchanged | unchanged | 409 `User.DuplicateEmail` now also raised when the identifier hash is reserved in `AccountDeletionTombstones` (same error as "taken" — no new information leak) |

New error members (house naming): `UserErrors.DeletionAlreadyRequested`, `UserErrors.AccountPendingDeletion(DateTime graceEndsAtUtc)`, `UserErrors.RecoveryWindowExpired`; `AccountDeletionErrors.InvalidOtp`; `ExternalAuthErrors` additions for Apple code-exchange failures.

---

## 7. Backend Work Breakdown

| ID | Files touched (● = `[TO BE CREATED]`) | Description | Acceptance criteria |
|---|---|---|---|
| B0 | ● `Auth_DB/dbo/Tables/Security/{AccountDeletionRequests, AccountDeletionTombstones, AccountDeletionVerifications, UserEncryptionKeys}.sql`; `Tables/Authentication/UserExternalLogins.sql` (+ column), `Tables/Core/Users.sql` (PhoneNumber widen); `Scripts/SeedData/09_ExternalAuthProviders.sql` (apple, disabled); `Auth_DB.sqlproj`; `UserRepository.HardDeleteAsync` (+ `UserEncryptionKeys` crypto-shred delete — forced immediately by the `UserHardDeleteSqlTests` FK guard) | Schema layer (§5) with named defaults, CKs, filtered unique active-request index; schema ships via the DACPAC publish diff (additive only — no upgrade script, per house convention) | DACPAC builds clean; publish diff is additive; filtered unique index rejects a second active request; `apple` row present and disabled; FK-guard tests green |
| B1 | ● `Auth.Domain/Entities/AccountDeletionRequest.cs`, ● `AccountDeletionTombstone.cs`; ● `Enums/AccountDeletionStatus.cs`, `…Source.cs`; `Errors/UserErrors.cs`, `Errors/ExternalAuthErrors.cs`, ● `Errors/AccountDeletionErrors.cs`; ● `Events/AccountDeletion{Requested,Cancelled,Completed}Event.cs`; ● `Interfaces/Repositories/IAccountDeletion{Request,Tombstone,Verification}Repository.cs`, ● `IUserEncryptionKeyRepository.cs` | Rich domain model: state transitions as behavior methods (`Claim()`, `Cancel()`, `Complete()`, `Fail()`) returning `ErrorOr`, private setters; immutable event records | Unit tests cover every legal/illegal transition; no anemic model; Domain references nothing |
| B2 | `Features/Users/DeleteUser/…`, `…HardDeleteUser/…`; ● `Features/Users/Common/OwnedOrganizationDeletionGuard.cs`; `Infrastructure/Persistence/UserRepository.cs`; ● `Application/Interfaces/ICredentialRevocationService.cs` + ● `Infrastructure/Authentication/CredentialRevocationService.cs`; `Features/Authentication/TerminateAllSessions/…` | DRY refactors: extract the duplicated owned-org guard; extract session+refresh+blacklist+IdpSessions revocation into one service; rework `HardDeleteAsync` into the unified staged destruction (tombstone MERGE → Apple revoke hook → DEK delete → anonymize logs → cascade A → reattribute → delete row) *(owner-approved behavior change)* | `UserHardDeleteSqlTests` extended and green: new tables in the FK guard; audit/login rows anonymized not deleted; tombstone + reservation written for admin `/permanent` too; existing admin flows otherwise identical |
| B3 | ● `Application/Interfaces/IPerUserCryptoService.cs`; ● `Infrastructure/Security/PerUserCryptoService.cs` + ● `UserEncryptionKeyRepository.cs`; `Infrastructure/Authentication/TwoFactorSecretProtector.cs` (v2); `Domain/Constants/SortFields.cs` + `Infrastructure/Persistence/UserRepository.cs` (drop phone sort; encrypt/decrypt PhoneNumber at the repository boundary); ● `Infrastructure/Security/EncryptionMigrationService.cs` (config-gated in-API one-shot — replaces the originally planned `Auth_Setup` command: shared-hosting prod offers no operator shell, and running in-process guarantees the exact Data Protection key ring and connection) | Per-user crypto foundation: lazy DEK per user, AES-256-GCM, field purpose as AAD, versioned ciphertext (`v2:` prefix) with dual-read of legacy values; one-time idempotent migration (2FA secrets + phone numbers) gated by `AccountDeletion:RunEncryptionMigration`, with a logged verify-count report | Roundtrip/tamper/wrong-user tests; v1 payloads still decrypt; migration idempotent (second run = 0 changes); phone displayed correctly in UI; phone sort removed from allowlist (400 on `sortBy=phoneNumber`, test updated) |
| B4 | ● `Application/Features/AccountDeletion/{RequestAccountDeletion, SendDeletionReauthCode, PublicRequestDeletion, ConfirmPublicDeletion, RecoverAccount, RecoverAccountExternal, ExecuteAccountDeletion}/…` (Command + Handler + Validator each); `Features/Authentication/{Login, ExternalLogin, Register}/…` (pending-deletion signal; reservation check); ● `Infrastructure/Persistence/AccountDeletion*Repository.cs`; ● identifier-hash helper (HMAC-SHA256 `ComputeTokenHash` convention, dedicated SecretManagement key) | Implement pipeline §3: guards → request row → soft delete → revoke all → events; recovery incl. 2FA TOTP and external id-token paths; OTP issuance/verification mirroring `EmailVerificationTokens` semantics (Argon2id, 5 attempts, 15 min) | All handlers `ErrorOr<T>`; FluentValidation on every command; anti-enumeration invariants proven by tests; idempotency: duplicate request → 409, duplicate public confirm → 202 |
| B5 | `Modules/UserManagement/Controllers/UsersController.cs`, `Modules/Authentication/Controllers/AuthController.cs` + ● request contracts (incl. `ExternalLoginRequest` new optional fields); ● `Modules/AuditLog/EventHandlers/AccountDeletion{Requested,Cancelled,Completed}AuditEventHandler.cs`; ● notification handlers `AccountDeletion{Requested,Cancelled,Completed}NotificationEventHandler.cs` | Thin endpoints via `ISender` (§6); audit actions `user.deletion_requested` / `user.deletion_cancelled` / `user.deletion_completed` (completed row: System actor, `{policyVersion, appleRevocation}` detail, zero PII); enqueue the **four** templated emails (cancellation confirmed by owner) | Endpoint ↔ contract table §6 exact; audit rows asserted in handler tests; no business logic in controllers |
| B6 | ● `Infrastructure/AccountDeletion/AccountDeletionWorker.cs`; `Auth_API/Program.cs` (AddHostedService + ● `AccountDeletionSettings`); `appsettings.json` | Worker per outbox pattern: startup reclaim + catch-up, poll 15 min, optimistic claim, per-user execution via scoped `ISender`, retry→`Failed` after 5; daily sweep (restore re-apply by UserId; expired verifications; LoginAttempts > 365d; Sent outbox > 180d); ERROR when >24h overdue | Overdue request executes on first poll; crash mid-`Processing` reclaimed; sweep idempotent; all intervals/retention config-driven (`AccountDeletion:*`) |
| B7 | ● `Infrastructure/Authentication/AppleAuthProvider.cs` (JWKS `appleid.apple.com/auth/keys`, iss/aud/exp/nonce validation — mirrors `GoogleAuthProvider`), ● `AppleClientSecretGenerator.cs` (ES256 JWT from the `.p8` key held in SecretManagement), ● `AppleTokenRevocationService.cs`; ● `Application/Interfaces/IExternalTokenLifecycle.cs` (`ExchangeCodeAsync`/`RevokeAsync` — implemented by Apple only, keeps `IExternalAuthProvider` unfattened per ISP); `ExternalAuthProviderFactory.cs` + DI registration; `ExternalLogin` handler (post-validate code exchange → encrypt+store refresh token); config `ExternalAuth:Apple:{ServicesId, TeamId, KeyId}` | Full Apple Sign-In: login/register/recover paths; refresh token stored A-E encrypted; revocation invoked at pipeline stage (d) and best-effort on request-time credential revocation | Provider resolves via factory (no type switches); token validation test-vectored (mock JWKS); exchange failure → login still succeeds but flagged (no token stored, WARN log); revocation success/failure/retry paths tested; nothing Apple-specific leaks into Domain |
| B8 | ● `SeedData/10_NotificationTypes.sql` (+4 types: `account-deletion-requested` {UserName, GraceEndsAt, GraceDays, RecoveryLink}; `account-deletion-verification` {UserName, OtpCode, ExpirationMinutes}; `account-deletion-cancelled` {UserName, CancelledAt}; `account-deletion-completed` {UserName}), `12_NotificationTemplates.sql` | Seed types (`IsSystem = 1` — published-template protection is DB-driven) + published v1 templates with all 7 language translations (house standard, not EN-only) following the existing GUID sequence and seed idempotence; ships to every environment via the post-deployment seed run — no Upgrades script (house convention keeps `Scripts/Upgrades/` for data reconciliation of already-seeded rows) | Types render in template editor; completion email enqueued with snapshot `Recipient` + `RecipientUserId = NULL` (verified supported); cancellation email delivered on recovery |

---

## 8. Background Jobs & Scheduling

| Job | Host | Trigger / cadence | Behavior | Failure mode |
|---|---|---|---|---|
| Grace executor (`AccountDeletionWorker`) | `BackgroundService` in Auth_API process (pattern: `NotificationOutboxDispatcher`) | Poll every 15 min + startup catch-up (reclaims `Processing` orphans, drains overdue backlog) | Claims due requests (`Status 1→3`, optimistic, single instance), runs staged destruction §3 incl. Apple revocation (stage d) and crypto-shred (stage e), marks `Completed` | Retry with `AttemptCount`; after 5 → `Failed` + ERROR log; >24h-overdue ERROR alarm; Apple-revoke failure never blocks completion past the attempt cap (recorded in audit detail) |
| Retention & destruction sweep | same worker, daily gate (first poll after UTC midnight) | Daily — **satisfies the KVKK ≤ 6-month periodic-destruction interval with margin** | (1) Re-apply: `Completed` request whose `UserId` matches a live `Users` row (backup restore) → re-execute destruction (R5); (2) purge expired `AccountDeletionVerifications`; (3) purge `LoginAttempts` older than 365d (confirmed); (4) purge `Sent` `NotificationOutbox` rows older than 180d (confirmed); audit row `system.retention_sweep` | Idempotent; partial failure resumes next day |
| Existing jobs (unchanged) | `TokenRevocationBackgroundService`, `NotificationOutboxDispatcher` | — | Deletion pipeline depends on both (blacklist persistence; email delivery) | — |

**Operational notes (rollout-blocking, see §12):** IIS app-pool preload/always-running is required (already required by the outbox — verified precedent). **Backups & restore runbook (R5):** hosting backup retention must be capped at ≤ 6 months; after ANY database restore, run the sweep immediately (worker does it automatically on startup/daily; the runbook mandates verifying `system.retention_sweep` post-restore); **crypto-shredding makes A-E fields (phone, TOTP secret, Apple token) unreadable in restored backups even before the sweep runs** — the DEK rows restored from backup are re-deleted by the re-apply pass; destruction evidence (`AccountDeletionRequests` terminal rows + tombstones + `user.deletion_completed` audit rows) is retained ≥ 3 years (confirmed) and is never swept.

---

## 9. Frontend Work Breakdown

All in `Auth_UI` (accounts app + shared packages). **Workflow per the `shadcn` skill:** components managed via the CLI against the verified `Auth_UI/components.json` (`pnpm dlx shadcn@latest add alert` run from `Auth_UI/`; review added files against the skill's critical rules before use). **Per the `find-skills` discipline:** before hand-rolling any capability, search the registries first (`pnpm dlx shadcn@latest search -q "…"`, `npx skills find …`) — never hand-roll an equivalent of an available component. Skill rules binding this work: `FieldGroup`/`Field` for all form layout (no `space-y-*`/raw divs); semantic tokens only, no color/typography overrides (house preset only); `Button` loading = `disabled` + spinner icon with `data-icon` (no `isLoading` prop); callouts = `Alert`, loading = `Skeleton`, statuses = `Badge` — never custom styled divs; `Dialog`/`AlertDialog` always carry a Title; `cn()` for conditional classes; CSS logical properties (RTL-safe). Every new string lands in all 7 locale files (parity test enforces); dates via the shared timezone/locale utilities.

| Screen | Element | Exact shadcn component | State & interaction |
|---|---|---|---|
| Profile → Account tab (`packages/account/src/pages/profile/` ● `profile-danger-zone.tsx`) | Danger Zone section | `Card` + `CardHeader`/`CardTitle`/`CardDescription`/`CardContent` (destructive accent via semantic tokens), `Button` `variant="destructive"` | Visible to every user (R6 prominence); explains 30-day grace; button opens re-auth step; `Skeleton` while `me` loads |
| Deletion flow — step 1: re-authentication | Modal | `Dialog` (with `DialogTitle`) + `Form` (`react-hook-form` + zod) + `FieldGroup`/`Field` + `Input type="password"` — passwordless accounts: `Button` "send code" then `InputOTP` (6 digits) | Submit disabled until valid; spinner icon `data-icon` + `disabled` while pending; server errors via `getErrorMessage` under the field (`data-invalid`/`aria-invalid`); focus-trapped |
| Deletion flow — step 2: typed confirmation | Destructive confirm | `AlertDialog` (`AlertDialogContent/Title/Description/Footer`) + `Field` + `Label` + `Input` | User must type their email exactly; confirm `Button` `variant="destructive"` disabled until match, loading state during POST; on 202 → `sonner` toast, client token purge, navigate to scheduled screen |
| `/deletion-scheduled` (● route, top-level like `/two-factor`) | Grace acknowledgment | ● `Alert` `[TO BE CREATED: pnpm dlx shadcn@latest add alert → packages/ui/src/alert.tsx]` + `Badge` + `Button` (link to `/login`) | Shows `graceEndsAtUtc` (locale/timezone-formatted) and recovery instructions from navigation state; direct visit → redirect `/login` |
| Login page (`apps/accounts/src/pages/auth/login.tsx`) *(modified)* | Pending-deletion branch | existing form; on `Auth.AccountPendingDeletion` → navigate `/account-recovery` with `{email, graceEndsAtUtc}` | No inline leak; unchanged behavior for all other errors |
| Login & register pages *(modified)* | Apple sign-in | ● `apple-sign-in.tsx` `[TO BE CREATED]` mirroring `google-sign-in.tsx` (singleton loader for Apple's `appleid.auth.js`, popup mode, nonce; posts `{provider:'apple', idToken, authorizationCode, givenName?, familyName?}`); `Separator` divider (existing pattern) | Renders only when `external-providers` lists an enabled `apple`; theme-aware; errors via `sonner` + `getErrorMessage` |
| `/account-recovery` (● page, top-level) | Countdown + restore | ● `Alert` + `Badge` (remaining days via existing `hooks/use-countdown.ts`) + `Button` "Restore my account" + (2FA accounts) `InputOTP` + (external accounts) provider button variant | Restore posts `/auth/deletion/recover` (or `recover-external`); success → tokens stored, `sonner` success toast (cancellation email arrives per B8), enter app; `User.RecoveryWindowExpired` → terminal `Alert` state |
| `/delete-account` (● public page, anonymous top-level route; **not** linked from any in-product screen — see note below) | Standalone request wizard (R6) | Step 1: `Card` + `Form` + `Field` + `Input type="email"` + `Button`; Step 2: `InputOTP` + `Button` (resend w/ cooldown `Badge`); Step 3: ● `Alert` generic confirmation | Always-generic messaging ("If an account exists…"); zod validation; loading/disabled states; `sonner` for transport errors; `Skeleton` between steps; fully keyboard-navigable; responsive |
| Shared | New i18n keys | — (`packages/i18n/src/{en,ar,tr,fr,zh,ur,fa}.ts`) | Type-enforced parity test fails the build if any locale misses a key |
| Shared (`packages/api`) | Env + contract | — | `APPLE_SERVICES_ID` added to `@astoom/api/env` beside `GOOGLE_CLIENT_ID`; generated API types refreshed for the new endpoints/fields |

**Entry-point decision (owner, 2026-07-27) — superseding the original "linked from login-page footer":** the public wizard is **not** linked from the sign-in screen or any other in-product surface. Rationale: a terminal destructive action does not belong on the authentication surface, where the dominant user intent is a forgotten password, not erasure; the placement also advertises an unauthenticated email-sending endpoint (bounded by rate limiting and anti-enumeration, but a needless deliverability/abuse amplifier). No regulation requires that placement — R6 requires the page to *exist and be publicly reachable*, which it is. Enforced by an E2E assertion that the sign-in page carries no deletion link.

**Policy-integrity hardening (owner, 2026-07-28):** every operational claim in the published policy is now backed by shipped code. (a) **Delivery-log redaction**: rendered bodies of sensitive types (`email-verification`, `password-reset`, `organization-invitation`, `ownership-transfer-code`, `account-deletion-verification` — `NotificationTypeCodes.SensitiveContentCodes`) are overwritten with `[redacted]` at rest the moment delivery succeeds, and the admin delivery-log read model never returns them in ANY status (an email-verification OTP signs the recipient in — admins must not be able to lift one from the outbox). (b) **Change-notification machinery**: `PrivacyPolicyVersions` registry table (+ seed 14, initial 2026.07), `privacy-policy-updated` system notification type + published 7-language template (seeds 10/12), `POST /privacy-policy/versions/notify` fanning out to every active confirmed user in their preferred language and stamping the version with time + recipient count (audit `system.policy_notification_sent`), console page **Notifications → Privacy Policy** (list/record/notify; notification-templates claims), gateway `privacy-policy-route`, plus an in-app update banner in the accounts shell keyed on `POLICY_VERSION` vs a locally acknowledged version. (c) **Wording precision**: retention-table claims verified against `AccountDeletionWorker.RunSweepAsync` (365d login attempts, 180d outbox, expired-OTP purge all real); backups row states the hosting-configured rotation explicitly; "same controls" phrasing made factual; every KVKK/GDPR/CCPA-CPRA mention links to the official text (mevzuat.gov.tr, EUR-Lex, leginfo). The sign-in screen's privacy link moved from under the card to the page footer.

**De-hardcoding the policy (owner, 2026-07-28) — two layers, both removed from source:** (a) **Numbers**: the policy quotes `{{graceDays}}`, `{{otpValidityMinutes}}`, `{{loginAttemptRetentionDays}}`, `{{outboxRetentionDays}}` as tokens; `GET /privacy-policy/published` (anonymous) returns them from the live `AccountDeletionSettings` on every request, so an appsettings change flows straight into the published text (verified live: OTP 15→7 and grace 30→45 appeared on the rendered page with no content edit). Statutory windows (KVKK/GDPR 30-day response, CCPA 45-day) stay literal — they come from law, not config. (b) **Content**: the policy document now lives in `PrivacyPolicyTranslations` per (version × language), authored in the console (Notifications → Privacy Policy → *Edit content*, per-language tabs, server-side document validation, `IsPublished` with a single-statement publish that requires the `en` document first). The bundled TS document is demoted to an **offline fallback** for when the API is unreachable, and is also the generator source for seed 16. (c) **Permissions**: the policy gets its own `privacy-policy:read` / `privacy-policy:manage` claims (seed 15, parent under `auth:*` so admin inherits; manage⇒read implication) — publishing legal text is a distinct duty from operating notification templates.

**Privacy-policy page (owner, 2026-07-28) — closes the discoverability gap:** ● `/privacy` (anonymous top-level route, `apps/accounts/src/pages/privacy/`) is a full 7-language privacy policy + KVKK Article 10 disclosure (**KVKK primacy per owner decision**: Türkiye rights first, KVKK Art. 5 legal bases cited alongside GDPR Art. 6, Art. 9 transfer regime, 30-day Başvuru Tebliği procedure; GDPR + CCPA/CPRA sections follow). Content is a typed document per locale (`content/{en,ar,tr,fr,zh,ur,fa}.ts`, `Record<LanguageCode, …>` enforces parity); the §5 retention table is embedded and MUST stay in sync with `AccountDeletionSettings`; `POLICY_VERSION` mirrors `PolicyVersion 2026.07`. The deletion section hosts the **"Delete my account"** destructive button (anonymous → `/delete-account`, signed-in → `/profile` danger zone). Owner-fillable facts (legal entity, address, privacy email, hosting/email providers; optional DPO/VERBİS/KEP) live in ONE file — `content/details.ts` — interpolated into every language; the page shows a draft warning until all required values are filled. The sign-in footer links `/privacy` (standard practice), which restores in-product discoverability of the wizard via the policy. E2E journey 3 walks login → privacy → delete wizard.

---

## 10. Security & Failure Modes

**Mandated edge cases**
- **Deletion during an active session elsewhere:** request revokes every `UserSession` (+ session-id blacklist → in-flight access tokens rejected in-process immediately), all `RefreshTokens`, all `IdpSessions`, and pending `AuthorizationCodes`; OIDC clients lose refresh capability at the IdP. Device B's next API call → 401.
- **Restore-after-delete (backup restore):** tombstones + terminal request rows exist in every backup taken after execution; sweep detects `Completed` request + live `Users` row by immutable `UserId` and re-executes destruction automatically; **A-E fields are already unreadable in the restored copy (DEK destroyed — crypto-shredding now genuinely satisfies R5)**; runbook (§8) mandates post-restore verification. Backups expire ≤ 6 months, bounding residual plaintext (identity columns) copies.
- **Partial pipeline failure:** all DB stages one transaction (idempotent: MERGE tombstone, guarded deletes); Apple revocation and post-commit steps (image file, completion email) retried via reclaim; 5 failures → `Failed` + ERROR alarm; nothing is half-visible because `IsDeleted = 1` since day 0 of grace.
- **Re-registration with a reserved identifier:** register / invitation-register / admin create all check `EmailHash`/`UsernameHash` against tombstones → `User.DuplicateEmail`, byte-identical to the "email taken" response (no deletion-state oracle). Auto-generated usernames re-roll on reservation collision.

**Additional catastrophic-impact edge cases**
- **Grace-end vs. recovery race:** recovery flips `Status 1→2` with row-count check; the worker claims `1→3` the same way — exactly one wins; loser gets `RecoveryWindowExpired` (deterministic after grace, per R1).
- **Concurrent duplicate requests (two devices / public+in-app):** filtered unique index is the source of truth → second gets 409 (in-app) or idempotent 202 (public).
- **2FA-enabled account recovery:** password alone never restores; `twoFactorCode` verified with the existing TOTP service in the same request (no challenge dance, no bypass).
- **Apple `/auth/revoke` failure or outage:** retried across execution attempts with the rest of the pipeline; after the cap, deletion proceeds (Apple refresh/access tokens expire naturally) and the destruction audit records `appleRevocation: failed` — deletion is never held hostage by a third party.
- **Apple private-relay emails (`…@privaterelay.appleid.com`):** treated as any other email — unique per user, reserved by hash on deletion; relay deactivation by Apple after revocation is expected and harmless.
- **Apple name only on first authorization:** `GivenName`/`FamilyName` accepted only when the external identity is not yet linked (first registration), sanitized and length-capped — replay of the fields against an existing account is ignored.
- **Code-exchange failure at Apple sign-in:** login still succeeds on a valid id_token; no refresh token stored (WARN logged) → revocation stage later no-ops for that user. Never blocks authentication.
- **Ciphertext tampering / cross-field swap:** AES-256-GCM auth tag + field purpose bound as AAD → decrypt fails closed (error, not garbage data).
- **DataProtection master material loss:** now affects DEKs, TOTP protector, HMAC reservation key → §4 risk 3; certificate/key-ring backup runbook extended; `KeyVersion` + versioned prefixes enable staged re-wrap if the certificate must rotate.
- **Encryption migration interrupted:** versioned prefixes make it resumable and idempotent; unmigrated rows keep working via dual-read.
- **Locked/admin-soft-deleted accounts:** lockout error precedence preserved in recovery; an admin-soft-deleted user (no request row) recovers nothing — pending-deletion signal requires an active request row, so admin deletions stay admin-only.
- **System account:** `WellKnownUserIds.System` guard at request time (flag alone is inert — verified).
- **Public OTP abuse:** gateway `auth` rate policy per IP + per-email issuance cap + 5 verify attempts + 15-min expiry; OTP is Argon2id-hashed at rest; unknown-email and wrong-OTP responses are identical (anti-enumeration).
- **Owned organizations:** same guard as admin deletion — request blocked with actionable 409 until ownership transferred; sole-member orgs deleted with the account (UI warns).
- **Email change during grace:** impossible — profile endpoints unreachable (`IsDeleted = 1` + revoked tokens).

**Bottleneck anatomy & bypass**
- **Destruction transaction breadth** (~27 statements under UPDLOCK): per-user row counts are small; worker processes users sequentially (no fan-out), keeping lock windows short; every statement hits an indexed `UserId` predicate; the Apple revoke (network I/O) runs **before** the DB transaction opens, so no lock is held across the network call.
- **Per-user decrypt cost on admin lists** (PhoneNumber): AES-GCM decrypt is microseconds/row at the capped page size (≤100); DEKs are cached per request scope; phone sort removal eliminates the only server-side ordering need on ciphertext.
- **Single-process worker on shared hosting:** sequential claiming removes concurrency hazards; throughput ceiling far above realistic volume; catch-up drains backlog bursts.
- **In-process token blacklist:** single-API-instance topology (verified) makes propagation immediate; `RevokedTokens` persistence covers recycles.

---

## 11. Test Matrix

Coverage target: **90% for all new code** (repo mandate; build-blocking).

| Level | Area | Tests (pattern: `[Method]_[Scenario]_[ExpectedBehavior]`, existing `Auth_API.Tests` layout) |
|---|---|---|
| Unit | Domain | `AccountDeletionRequest` transitions: Claim/Cancel/Complete/Fail legal + illegal paths; grace computation |
| Unit | Application | Every handler: happy path; system-user guard; owned-org 409s; duplicate-request 409; OTP wrong/expired/attempt-cap; anti-enumeration equality (unknown email ≡ wrong OTP; recover w/o pending ≡ invalid credentials); 2FA recovery required; external recovery (google + apple); login/external-login pending-deletion signal; register reservation rejection; ExecuteAccountDeletion stage ordering (revoke → shred → anonymize → cascade) + event publication (mocked repos, Moq + FluentAssertions per house style) |
| Unit | Apple provider | JWKS signature/iss/aud/exp/nonce validation (mock keys); client-secret generator claims (iss=TeamId, sub=ServicesId, ES256); code-exchange success/failure (no token stored, login unaffected); revocation success/failure/retry; factory resolves `apple` with no type switches |
| Unit | Per-user crypto | Encrypt/decrypt roundtrip; tamper → fail closed; AAD purpose mismatch → fail; wrong user's DEK → fail; lazy DEK creation; v1→v2 dual-read (TOTP protector); migration idempotence (second run = 0 changes) |
| Unit | Event handlers | 4 audit + 3 notification handlers: correct `Action`/attribution (completed = System actor, zero PII, `appleRevocation` detail), correct template type/variables incl. `account-deletion-cancelled` |
| Unit | Worker | Claim semantics, retry→Failed at 5, overdue-alarm emission, daily sweep gating (time provider abstraction) |
| Integration (SQL) | `UserHardDeleteSqlTests` (extended) + ● `AccountDeletionSqlTests` | FK/schema-drift guard includes 4 new tables + new column; unified purge leaves zero rows keyed to the user except anonymized AuditLogs/LoginAttempts + tombstone + terminal request; **UserEncryptionKeys row provably deleted (crypto-shred assert)**; anonymization asserts; tombstone MERGE idempotent; filtered unique active-request index; restore re-apply (reinsert user + DEK → sweep purges both again) |
| Integration | Gateway | `GatewayRouteCoverageTests` still green (new endpoints under existing `auth`/`users` prefixes — asserts no uncovered route) |
| Frontend unit | packages | New `Alert` component render/theming; danger-zone dialog state machine (disabled-until-typed, loading); apple-sign-in loader/render gating on provider list; countdown formatting; locale parity test (automatic) |
| E2E (Playwright, `Auth_UI/e2e`) | Full journeys | (1) request in-app w/ password → logged out → login shows recovery → restore → logged in **+ cancellation email row in outbox**; (2) passwordless OTP request path (OTP read from Serilog dev log per established E2E practice); (3) public `/delete-account` wizard incl. generic-response assertions; (4) post-grace login after worker execution (grace shortened via config) → invalid credentials; re-register blocked; (5) Apple flow with a stubbed provider (real Apple endpoints mocked at the provider seam — no live Apple in CI) |

---

## 12. Rollout & Migration Order (strict — no step may be skipped)

1. **DB forward-compatible layer:** publish DACPAC with the 4 new tables, `UserExternalLogins.ProviderRefreshTokenEnc`, widened `Users.PhoneNumber`, apple provider row (disabled); the post-deployment script applies the seeds (incl. the 4 notification types/templates from B8) idempotently on the same publish. *(Old code ignores all of it — zero risk.)*
2. **Backend refactor wave (B1–B2):** Domain + shared guard/revocation extraction + unified destruction routine; extended SQL tests green **before** any new endpoint exists. *(Admin `/permanent` now writes tombstones + reservations and anonymizes logs — owner-approved; verify on staging with a throwaway account.)*
3. **Crypto foundation (B3):** `PerUserCryptoService` + `UserEncryptionKeys` + TOTP protector v2 + phone encryption at the repository boundary + `SortFields` change; deploy with dual-read active; **enable `AccountDeletion:RunEncryptionMigration` for one deployment on staging, verify the logged counts + a 2FA login rehearsal, disable the flag, then repeat on prod**.
4. **Feature wave (B4–B6, B8):** deletion commands, endpoints, event handlers, 4 templates, worker, config (`AccountDeletion:*` incl. dedicated HMAC key provisioned via SecretManagement on the server, not in git).
5. **Gateway:** no config change; deploy in lockstep with the API anyway (shared secrets contract); confirm coverage tests in CI.
6. **API deployment (prod):** deploy Auth_API + gateway; verify app-pool always-running/preload; watch Serilog for worker start + first poll.
7. **Apple prerequisites (owner/ops, can run in parallel from step 1):** Apple Developer Program — App ID + **Services ID** (client id), domain verification + return URLs for the accounts domain, `.p8` signing key → provisioned via SecretManagement; set `ExternalAuth:Apple:*` + frontend `APPLE_SERVICES_ID`; **then** flip the seeded provider row to `IsEnabled = 1` (B7 code deploys with the feature wave; the button and provider stay dormant until this flip).
8. **Frontend:** `Alert` via shadcn CLI → i18n keys (7 locales) → danger zone + flow dialogs → recovery + scheduled screens → public page + login branch + apple-sign-in; deploy SPA after the API is live.
9. **Activation validation (staging then prod):** scripted pass of E2E journeys (1)–(3); confirm audit rows, all four outbox emails, tombstone row, crypto-shred (DEK row gone), reservation rejection; Apple sign-in + revocation smoke test with a real Apple test account.
10. **Operational hardening:** hosting backup retention ≤ 6 months; extend the DataProtection certificate/key-ring backup runbook to name the new dependents (DEKs, HMAC key); add the post-restore re-apply verification step to the restore runbook. **Privacy-policy disclosure (R6): SHIPPED in-app** — `/privacy` carries `PolicyVersion 2026.07`, the §5 retention table and the "Delete my account" entry point in all 7 languages (see the §9 note). Remaining owner steps: (a) fill `apps/accounts/src/pages/privacy/content/details.ts` (legal entity, address, monitored privacy email, hosting/email providers; optional DPO, VERBİS no., KEP) — the page shows a draft banner until done; (b) legal review of the policy text (KVKK primacy) before go-live; (c) set the app-store listing's data-deletion URL to `https://accounts.astoom.com/delete-account` (or `/privacy`).
11. **Definition-of-done gate:** §13 checklist fully checked; coverage report ≥ 90% new code; `/final-review-checklist` §10 compliance pass.

---

## 13. Assumptions & Open Questions + Definition of Done

### Resolved decisions (owner, 2026-07-27)
1. **Approved:** admin `/permanent` behavior change — tombstone + permanent identifier reservation + anonymize-instead-of-delete logs.
2. **Confirmed:** retention constants — grace 30d; LoginAttempts 365d; outbox 180d; destruction evidence 3y; `PolicyVersion` format `YYYY.MM` (initial `2026.07`).
3. **Confirmed:** cancellation sends an email — fourth template `account-deletion-cancelled` + audit row.
4. **Added to scope:** Apple Sign-In (full integration + deletion-time token revocation). The repo's strategy layer and schema comments were already designed for it (verified §2).
5. **Added to scope:** per-user encryption at rest (AES-256-GCM DEK per user, DataProtection-wrapped) applied to `Users.PhoneNumber`, `TwoFactorAuth.SecretKey` (protector v2), and the Apple refresh token; DEK destruction = genuine crypto-shredding (R5 now fully satisfied). Identity/lookup columns stay plaintext by design (§1 Non-Goals).
6. **Acknowledged:** no financial/invoice data exists — R2's financial Class C branch is vacuously satisfied.

### Assumptions
- Owner holds (or will obtain) Apple Developer Program membership and can create the Services ID, verify the accounts domain, and issue the `.p8` key — prerequisites for §12 step 7; until then the seeded provider stays disabled and the button never renders.
- Hosting (Plesk/IIS) backup retention can be capped at ≤ 6 months and app-pool preload/always-running is (or will be) enabled — operational, not repo-verifiable.
- The accounts SPA qualifies as the "standalone public web page" (R6): `/delete-account` is reachable without any authenticated app context.
- Acknowledgment ≤ 30 days (R6) is satisfied structurally: acknowledgment immediate (202 + email), completion exactly at grace end.
- Public deletion for unconfirmed-email accounts still requires email possession (OTP) — the email is the only provable identifier.

### Open questions (non-blocking for implementation start)
1. **Phone sort/search removal** — encrypting `Users.PhoneNumber` removes `phoneNumber` from the users-list sort allowlist (console admin table). Flagged for explicit acknowledgment; the alternative (keep phone plaintext) weakens decision 5.
2. **Apple go-live date** — step 7 credentials gate the provider flip; everything else ships dormant.

### Definition of Done
- [ ] All §7 tasks B0–B8 merged; one feature commit series per house convention
- [ ] R1–R7 each traceable to shipped code paths (map in PR description) — including R5 crypto-shredding and R6 Apple revocation, now in scope
- [ ] `UserHardDeleteSqlTests` + `AccountDeletionSqlTests` (incl. crypto-shred assert) + `GatewayRouteCoverageTests` + locale-parity + full suite green
- [ ] Coverage ≥ 90% on new code, report attached
- [ ] E2E journeys 1–5 (§11) pass against staging
- [ ] Encryption migration (`AccountDeletion:RunEncryptionMigration`) executed on staging and prod with the logged verify-count report; 2FA login rehearsal passed post-migration; flag disabled afterwards
- [ ] Apple: Services ID configured, provider row enabled, sign-in + deletion-time revocation smoke-tested with a real Apple account
- [ ] Prod: DB upgraded, seeds applied (4 templates), HMAC key provisioned, worker running (Serilog evidence), backup retention ≤ 6 months confirmed, DataProtection backup runbook extended, restore runbook updated
- [ ] Privacy-policy retention disclosure updated (`PolicyVersion 2026.07`) and linked from both deletion surfaces
- [ ] `/final-review-checklist` §10 architectural-compliance pass recorded
