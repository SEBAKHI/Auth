# ADR 0001: One error contract for the Auth API

## Status

Accepted — 2026-09-26. Supersedes the error format described in `ReadMe/DEVELOPER_GUIDE.md` §4.3 before this date. **Breaking change** for every client that read the error code from `title`.

## Context

Before this decision the two hosts (`Auth_API`, `API_Gateway`) wrote errors in eleven shapes:

- `ApiController.Problem` put the ErrorOr code in `title` (a human-readable member under RFC 9457 §3.1.3) and added `errors[{code, description}]` only when a request failed two or more rules, so a single error had no stable machine member at all. Clients guessed the code from `title` ("a title with no space is a code"), which breaks on one-word titles such as `Forbidden` and on languages that do not separate words with spaces.
- FluentValidation failures used the **property name** as the code (`Password`), so one code meant different rules on different operations (a length ceiling on sign-in, the new account's password on registration).
- Two different 429 bodies (`{status, error, retryAfter}` from the API, `{type, title, status, detail, retryAfter}` from the gateway), `{error}` from image upload, `ValidationProblemDetails` with an `errors` dictionary from model binding, `{field, message}` entries from the exception middleware, and **empty bodies** for 401, 403, 404, 405, 415 and the gateway's 502/504. The SPA reads the status from the body, so an empty body lost the error's kind.

The repository skills make one contract mandatory: `/dotnet-architecture` P7 and `/backend-development` → Response Format, with the catalog rules in `/domain-driven-design` §5 and the client rules in `/frontend-playbook` `rules/api-and-state.md`.

## Decision

### 1. The body

Every error response is `application/problem+json` (RFC 9457):

| Member | Rule in this API |
|---|---|
| `code` | Always present. A handler error carries its catalog code unchanged; a framework-produced error carries a transport code; an authentication challenge may carry a challenge reason. |
| `errors` | Only on a **Validation** result with **two or more** failures: `[{ "code", "pointer"? }]`, in rule-declaration order, `errors[0].code == code`, no text member. Absent everywhere else. |
| `status` | Equals the status line. |
| `type`, `title` | The framework defaults (RFC 9110 section link and standard title; 429 gets its reason phrase and no `type`). Never set by this API. |
| `detail` | The catalog sentence for `code`, localized by `Accept-Language` in one place (`ProblemText`). Never exception text. |
| `instance` | The request path. |
| `traceId` | Added by the framework. |
| Other | None. `Retry-After` travels as a header (429, 503), never in the body. |

### 2. One writer

- `AddProblemDetails` with **one** `CustomizeProblemDetails` (shared by both hosts, `Auth.Shared/Http/ErrorContract`, wired by `AddErrorContract`/`UseErrorContract`). It sets `instance`, and it is the only author of `code` and `detail`:
  - `code` is the code the request recorded in `HttpContext.Items` (a handler's first error, a middleware's reason, an exception translation), else the transport code for the status. A `code` that a library wrote itself (for example Asp.Versioning's) is replaced.
  - `detail` is that code's sentence from `DomainErrors`, which holds one sentence per published code, transport codes included, in the request's culture (`ProblemText`), with `Content-Language`.
  - A 503 without `Retry-After` gets `ErrorContract:Outage:RetryAfterSeconds` (default 30).
  - It never touches `type` or `title`, and replaces a minimal-API validation problem with a plain one, so there is no dictionary-shaped `errors`.
- `UseExceptionHandler` with **one** `IExceptionHandler` (`ErrorContractExceptionHandler`). A host translates a driver exception through an `IExceptionProblemTranslator` keyed by the exception type (the SQL translator lives in `Auth_API`, since `Auth.Shared` references no driver). `ExceptionHandlingMiddleware` and `GatewayExceptionMiddleware` are removed.
- `UseStatusCodePages` writes every empty 4xx/5xx body: authentication challenges, authorization denials, unmatched routes, wrong methods and media types, the rate limiter's 429 and YARP's 502/504.
- The rate limiters, `JwtBlacklistValidationMiddleware`, `GatewayTokenValidationMiddleware` and `RequireAdminApiEnabledAttribute` set a status (and, where it adds meaning, a reason code) and write no body of their own.
- Handler results reach HTTP through one mapper (`ProblemMapping`) and one status map (`ErrorStatusMap`); MVC's model-state 400 goes through `ProblemDetailsFactory` with `Http.BadRequest` and no `errors`.

### 3. Codes

- **Published list:** [`docs/api/error-codes.json`](../api/error-codes.json). A code is added there before it is emitted, and once published it is never renamed, removed or reused. Build-failing tests hold the catalog, the transport codes and every validator rule to the list.
- **Catalog codes** keep their `{Concept}.{Name}` strings. Codes that handlers or entities raised inline (`Organization.InvitationNotPending`, `User.RoleAlreadyAssigned`, …) move into their `{Concept}Errors` class with the same string. `ApiKey.AlreadyRevoked` is now 409 on both revoke and rotate (rotate returned 400).
- **Transport codes:** `Http.BadRequest` (400 and any other framework 4xx), `Http.Unauthenticated` (401), `Http.Forbidden` (403), `Http.NotFound` (404), `Http.MethodNotAllowed` (405), `Http.ContentTooLarge` (413), `Http.UnsupportedMediaType` (415), `Http.RateLimited` (429), `Http.Unavailable` (502, 503, 504), `Http.Unexpected` (500 and any other 5xx).
- **Challenge reasons** (401): `Http.TokenExpired` (JwtBearer challenge on an expired token), `Http.TokenRevoked` and `Http.SessionRevoked` (JwtBlacklistValidationMiddleware).

### 4. Validation codes and field-level validation

This API **publishes field-level validation**:

- Every FluentValidation rule declares `.WithErrorCode(...)` with a catalog code. A property name is never a code. The validation behavior returns `Error.Validation(code: failure.ErrorCode, description: failure.ErrorMessage, metadata: { property })`.
- A code concerns **one** request-body member, named in the published list as an RFC 6901 pointer (`#/newPassword`), or `null` when it concerns none, a query parameter, or a member whose path varies (nested collections, the password policy). This is why a shared rule applied to differently named members was split into one code per member (appendix).
- `errors[].pointer` is computed per request from the offending property path, and only when the action's `[FromBody]` type has that member; otherwise the entry has no pointer.
- Rules that had no resource key (FluentValidation defaults, plain English) now have codes and translated sentences.
- Localization is looked up by code: `ValidationMessages` is merged into `DomainErrors`, keyed by code.
- **No DataAnnotations and no C# `required` on requests.** An attribute fails in model binding and a `required` member fails in the JSON reader, and both answer `Http.BadRequest` with no rule code. MVC's implicit `[Required]` on non-nullable members is switched off for the same reason. The 17 authentication request contracts lost their attributes; every constraint they expressed is a validator rule, and a test keeps attributes from returning:
  - rules that already existed kept their codes;
  - new rules with existing codes: `Provider` ≤ 50 on external sign-in (`ExternalAuth.ProviderTooLong`), and the address rules (`Email.*`, ≤ 254) on the public deletion and account-recovery requests;
  - new codes: `Password.ConfirmationMismatch` (the `[Compare]` on `confirmNewPassword`, now carried into the change- and reset-password commands), and `ExternalAuth.GivenNameTooLong`, `ExternalAuth.FamilyNameTooLong` (stored in `NVARCHAR(100)` columns) and `ExternalAuth.AuthorizationCodeTooLong` (forwarded to the provider);
  - deliberately without a rule: `pendingId` (an empty or unknown handle already gets the merged `EmailVerification.InvalidOrExpiredOtp`, by design against enumeration, and is never stored), `deviceId` (bounded to 64 by `ApiController.GetDeviceId`), and `nonce` and `twoFactorCode` on external sign-in and recovery (compared, never stored).

**Why `errors` only for two or more failures:** this is the text of the skill. Moving later to "`errors` on every Validation result" is additive and non-breaking (one condition in the mapper, one assertion, one skill paragraph); the reverse would remove a member clients rely on. The conditional form keeps that option open.

### 5. Exceptions

| Exception | Status | Code |
|---|---|---|
| Dependency outage: `HttpRequestException`, `TimeoutException`, `TaskCanceledException` whose inner exception is a `TimeoutException`, `SqlException` numbers -2, 2, 53, 233, 4060, 10053, 10054, 10060, 40613 (timeout, network, database unavailable) | 503 + `Retry-After` | `Http.Unavailable` |
| `SqlException` 547 (a reference blocks a hard delete) | 409 | `Persistence.ReferenceConflict` |
| Anything else, including `SqlException` 2601/2627, `KeyNotFoundException`, `InvalidOperationException`, `ArgumentException` | 500 | `Http.Unexpected` |

- **547 stays a 409:** the hard-delete paths (roles, permissions, notification templates) may not check references first, and an administrator needs "in use elsewhere", not "unexpected error".
- **2601/2627 are 500:** the races that are expected are already translated where they occur (for example `UserRepository` → `DuplicateEmail`). A blanket 409 hid a deterministic defect before (the `UQ_Users_Username` local-part collision), so any new unique violation must surface as a fault.
- **No Polly entry:** the skill's default outage list includes Polly's `ExecutionRejectedException`; no project here references Polly, so the classifier leaves it out until one does.
- **The other three were 404/400:** they are programming errors; only `FluidTemplateRenderer` throws `KeyNotFoundException`, and `InvalidOperationException` is thrown by entity guards. A 4xx blamed the client and hid the fault from error monitoring.

### 6. Status map

| ErrorType | Status |
|---|---|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure, Unexpected | 500 |

A result carries errors of one ErrorType; the status comes from the first error.

### 7. Migration

One cut, no version in which the API emits two formats. `Auth_UI` moves in the same change: it reads `code` only, takes the status from the transport, and places field errors from `errors[].pointer` or, for a single failure, from the published pointer of its code. External integrators that branched on `title` must switch to `code` (see the table in `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`).

Members that disappear from error bodies:

- `title` no longer carries the code; it is the framework's reason phrase.
- `errors[].description` and `errors[].field`/`message` (exception bodies): entries are `{ code, pointer? }`.
- `correlationId` (an echo of `X-Correlation-ID` on exception bodies) and the Development-only `exception` member: `traceId` identifies the request, and exception data never reaches a body.
- The `{ error }` body of the image upload and the ad-hoc 429 bodies, with their `retryAfter` member: `Retry-After` is a header.

The `Token-Expired: true` header on a 401 for an expired token stays, beside the code `Http.TokenExpired`.

**In `Auth_UI`:** the client middleware passes every failed response through `readProblem`, so a failure carries the transport status even with an empty or non-JSON body. Codes are checked against a map generated from the published list (`pnpm gen:error-codes`, held to the list by `error-codes.test.ts`), and the `PublishedErrorCode` type makes every code a page branches on a compile-time check. The map is a chunk of its own, loaded on the first failure and failing closed (an unknown code reads by its status): inline, it added about 20 kB to every entry and pushed `/` and `/users` past their payload budgets.

## Consequences

### Positive

- One read path for the code: `body.code`, on every error response from either host.
- A code means one rule; clients branch and translate on it safely.
- Empty bodies are gone, so the SPA classifies 401/403/404/405/502 correctly.
- Validation limits now match their columns (appendix), turning five latent truncation 500s into 400s.

### Negative

- Breaking for integrators that read `title` or the `{error}` upload body.
- The published pointer of a code is static; a code whose member path varies has `null`, and its single failure is shown at form level.
- More codes (one per member where members differ).

### Neutral

- `type` and `title` are now the framework's; they may change with the .NET version, and tests capture them from a real run.

## Alternatives Considered

### Alternative A: patch every writer

Each of the eleven writers builds the new body by hand. Rejected: the skill forbids hand-written bodies, and the drift returns with the next writer.

### Alternative B: rewrite responses at the edge

A middleware buffers every error response and rewrites it. Rejected: it moves the guessing from the client to the server, cannot recover a code that was never sent, and buffers every error body.

### Alternative C: keep 409 for unique violations

Rejected for the reason in §5.

## Deferred (tracked, not in this change)

- Integration contract tests on `WebApplicationFactory<Program>` for every error path (requires `public partial class Program` and fakes for external adapters).
- The OpenAPI `ProblemDetails` schema transformer (`code`, `errors`, `traceId`) and `check:api` in CI.
- Per-domain closed code lists at UI call sites (`/frontend-playbook` step 3).
- Converting existing catalog properties (`=>`) to `static readonly` fields.

## References

- RFC 9457, Problem Details for HTTP APIs.
- `/backend-development` → Response Format; `references/error-contract-pipeline.md`.
- `/domain-driven-design` §5 Domain Errors.
- `/frontend-playbook` `rules/api-and-state.md`.

## Appendix: validation code migration

Before this change a validation failure carried the **property name** as its code; the resource key below was only used to translate `detail`. Each rule now carries the code in the second column.

Limit fixes: `ContactEmail` 256 → 254 (column `NVARCHAR(255)`); organization `Website` on update and `BaseUrl`/`LogoUrl`/`LogoUrlDark`/`FaviconUrl` 2048 → 500 (columns `NVARCHAR(500)`); webhook `TargetUrl` 2048 → 2000 (column `NVARCHAR(2000)`); organization `Description` on update 500 → 1000 (column `NVARCHAR(1000)`, as on create).

| Old resource key (was only in `detail`) | New code | `pointer` | Note |
|---|---|---|---|
| `Validation.Email.Required` | `Email.Required` | `#/email` |  |
| `Validation.Email.InvalidFormat` | `Email.InvalidFormat` | `#/email` | same string the Email value object already uses |
| `Validation.Email.MaxLength` | `Email.TooLong` | `#/email` | same string as the value object; text said 256, rule is 254 |
| `Validation.ContactEmail.Required` | `ContactEmail.Required` | `#/contactEmail` | now also used by applications and organization update, which used Email.* |
| `Validation.ContactEmail.InvalidFormat` | `ContactEmail.InvalidFormat` | `#/contactEmail` |  |
| `Validation.ContactEmail.MaxLength` | `ContactEmail.TooLong` | `#/contactEmail` | LIMIT FIX: create allowed 256 but the column is NVARCHAR(255) |
| `Validation.Password.Required` | `Password.Required` | `#/password` |  |
| `Validation.Password.MaxLength` | `Password.TooLong` | `#/password` | SPLIT by field |
| `Validation.Password.MaxLength` | `Password.CurrentTooLong` | `#/currentPassword` | SPLIT by field |
| `Validation.Password.MaxLength` | `Password.NewTooLong` | `#/newPassword` | SPLIT by field |
| `Validation.CurrentPassword.Required` | `Password.CurrentRequired` | `#/currentPassword` |  |
| `Validation.NewPassword.Required` | `Password.NewRequired` | `#/newPassword` |  |
| `Validation.NewPassword.MustDiffer` | `Password.NewMustDiffer` | `#/newPassword` |  |
| `Validation.Password.TooShort` | `Password.TooShort` | — | EXISTING code, unchanged; policy code: field varies (password/newPassword), see errors[].pointer |
| `Validation.Password.RequiresUppercase` | `Password.RequiresUppercase` | — | EXISTING code, unchanged |
| `Validation.Password.RequiresLowercase` | `Password.RequiresLowercase` | — | EXISTING code, unchanged |
| `Validation.Password.RequiresDigit` | `Password.RequiresDigit` | — | EXISTING code, unchanged |
| `Validation.Password.RequiresSpecialCharacter` | `Password.RequiresSpecialCharacter` | — | EXISTING code, unchanged |
| `Validation.Password.CommonPattern` | `Password.CommonPattern` | — | EXISTING code, unchanged |
| `Validation.PhoneNumber.MaxLength` | `PhoneNumber.TooLong` | `#/phoneNumber` |  |
| `Validation.FirstName.Required` | `User.FirstNameRequired` | `#/firstName` |  |
| `Validation.FirstName.MaxLength` | `User.FirstNameTooLong` | `#/firstName` |  |
| `Validation.LastName.Required` | `User.LastNameRequired` | `#/lastName` |  |
| `Validation.LastName.MaxLength` | `User.LastNameTooLong` | `#/lastName` |  |
| `Validation.PreferredLanguage.NotSupported` | `User.PreferredLanguageNotSupported` | `#/preferredLanguage` |  |
| `Validation.Theme.NotSupported` | `User.ThemeNotSupported` | `#/theme` |  |
| `Validation.TimeZone.Invalid` | `User.TimeZoneInvalid` | `#/timeZone` | SPLIT: body field of a user |
| `Validation.TimeZone.Invalid` | `Dashboard.TimeZoneInvalid` | — | SPLIT: dashboard query parameter |
| `(unkeyed NotEmpty) Dashboard TimeZone` | `Dashboard.TimeZoneRequired` | — | NEW: rule had no code |
| `Validation.UserId.Required` | `User.IdRequired` | — |  |
| `Validation.LockDuration.GreaterThanZero` | `User.LockDurationNotPositive` | `#/lockDurationMinutes` |  |
| `Validation.Reason.Required` | `User.LockReasonRequired` | `#/reason` |  |
| `Validation.Reason.MaxLength` | `User.LockReasonTooLong` | `#/reason` |  |
| `Validation.Code.Required` | `Code.Required` | `#/code` | applications, roles, permissions |
| `Validation.Code.MaxLength100` | `Code.TooLong` | `#/code` |  |
| `Validation.Code.InvalidFormat` | `Code.InvalidFormat` | `#/code` |  |
| `Validation.PermissionCode.MaxLength` | `PermissionCode.TooLong` | `#/code` |  |
| `Validation.PermissionCode.InvalidFormat` | `PermissionCode.InvalidFormat` | `#/code` |  |
| `Validation.Name.Required` | `Name.Required` | `#/name` | api keys, applications, permissions, roles, webhook keys |
| `Validation.Name.MaxLength` | `Name.TooLong` | `#/name` |  |
| `Validation.Name.Required` | `SystemSettings.PlatformNameRequired` | `#/platformName` | SPLIT by field |
| `Validation.Name.MaxLength` | `SystemSettings.PlatformNameTooLong` | `#/platformName` | SPLIT by field |
| `Validation.Description.MaxLength` | `Description.TooLong` | `#/description` |  |
| `Validation.PageNumber.Min` | `Paging.PageNumberOutOfRange` | — |  |
| `Validation.PageSize.Range` | `Paging.PageSizeOutOfRange` | — |  |
| `(unkeyed) GetLoginHistory Take` | `Paging.TakeOutOfRange` | — | NEW: rule had plain English |
| `Validation.SortBy.NotAllowed` | `Sorting.SortByNotAllowed` | — |  |
| `Validation.SearchTerm.MaxLength` | `Search.TermTooLong` | — |  |
| `Validation.Days.Range` | `Dashboard.DaysOutOfRange` | — |  |
| `Validation.HorizonDays.Range` | `Dashboard.HorizonDaysOutOfRange` | — |  |
| `Validation.OrganizationCode.Required` | `Organization.CodeRequired` | `#/code` |  |
| `Validation.OrganizationCode.MaxLength` | `Organization.CodeTooLong` | `#/code` |  |
| `Validation.OrganizationCode.InvalidFormat` | `Organization.CodeInvalidFormat` | `#/code` |  |
| `Validation.OrganizationName.Required` | `Organization.NameRequired` | `#/name` | create and update (update used Name.*) |
| `Validation.OrganizationName.MaxLength` | `Organization.NameTooLong` | `#/name` |  |
| `Validation.Description.MaxLength1000` | `Organization.DescriptionTooLong` | `#/description` | create and update; LIMIT FIX: update capped at 500, column is NVARCHAR(1000) |
| `Validation.WebsiteUrl.MaxLength` | `Organization.WebsiteTooLong` | `#/website` | LIMIT FIX: update allowed 2048, column is NVARCHAR(500) |
| `Validation.Url.MaxLength` | `Organization.LogoUrlTooLong` | `#/logoUrl` | SPLIT + LIMIT FIX (2048 -> 500, column NVARCHAR(500)) |
| `Validation.OrganizationId.Required` | `Organization.IdRequired` | — |  |
| `Validation.InvitationId.Required` | `Organization.InvitationIdRequired` | — |  |
| `Validation.Token.Required` | `Organization.InvitationTokenRequired` | `#/token` | SPLIT: accept invitation, register with invitation, get invitation |
| `Validation.NewOwnerId.Required` | `Organization.NewOwnerIdRequired` | `#/newOwnerId` |  |
| `Validation.TransferCode.InvalidFormat` | `Organization.TransferCodeInvalidFormat` | `#/code` |  |
| `(unkeyed) SubscriptionTier MaximumLength(50)` | `Organization.SubscriptionTierTooLong` | `#/subscriptionTier` | NEW: rule had no code |
| `Validation.Url.MaxLength` | `Application.BaseUrlTooLong` | `#/baseUrl` | SPLIT + LIMIT FIX (2048 -> 500) |
| `Validation.Url.MaxLength` | `Application.LogoUrlTooLong` | `#/logoUrl` | SPLIT + LIMIT FIX (2048 -> 500) |
| `Validation.ReauthenticationMaxAge.Range` | `Application.ReauthenticationMaxAgeOutOfRange` | `#/reauthenticationMaxAgeMinutes` |  |
| `Validation.RedirectUri.Invalid` | `Application.RedirectUriInvalid` | `#/redirectUris` |  |
| `Validation.RedirectUri.TooMany` | `Application.RedirectUrisTooMany` | `#/redirectUris` |  |
| `(unkeyed) SessionTimeoutMinutes GreaterThan(0)` | `Application.SessionTimeoutNotPositive` | `#/sessionTimeoutMinutes` | NEW: rule had no code |
| `(unkeyed) MaxConcurrentSessions GreaterThan(0)` | `Application.MaxConcurrentSessionsNotPositive` | `#/maxConcurrentSessions` | NEW |
| `(unkeyed) AccessMode IsInEnum` | `Application.AccessModeInvalid` | `#/accessMode` | NEW |
| `Validation.ApplicationId.Required` | `Application.IdRequired` | — |  |
| `(unkeyed) GrantApplicationAccess Note` | `Application.AccessNoteTooLong` | `#/note` | NEW |
| `Validation.ExpirationDate.Future` | `Expiry.NotInFuture` | `#/expiresAt` | api keys, webhook keys, organization applications, application access |
| `Validation.ApiKey.Required` | `ApiKey.Required` | — |  |
| `Validation.ApiKey.InvalidPrefix` | `ApiKey.InvalidPrefix` | — |  |
| `Validation.RateLimitPerMinute.GreaterThanZero` | `ApiKey.RateLimitPerMinuteNotPositive` | `#/rateLimitPerMinute` |  |
| `Validation.RateLimitPerDay.GreaterThanZero` | `ApiKey.RateLimitPerDayNotPositive` | `#/rateLimitPerDay` |  |
| `Validation.Environment.Required` | `Environment.Required` | `#/environment` | api keys and webhook keys |
| `(unkeyed) Environment MaximumLength(50)` | `Environment.TooLong` | `#/environment` | NEW |
| `Validation.GracePeriod.NonNegative` | `KeyRotation.GracePeriodNegative` | `#/gracePeriodMinutes` | api keys and webhook keys |
| `Validation.WebhookKey.Required` | `WebhookKey.Required` | — |  |
| `Validation.WebhookKey.InvalidPrefix` | `WebhookKey.InvalidPrefix` | — |  |
| `Validation.WebhookKeyId.Required` | `WebhookKey.IdRequired` | — |  |
| `Validation.Url.MaxLength` | `WebhookKey.TargetUrlTooLong` | `#/targetUrl` | SPLIT + LIMIT FIX (2048 -> 2000, column NVARCHAR(2000)) |
| `Validation.Url.MaxLength` | `SystemSettings.LogoUrlTooLong` | `#/logoUrl` | SPLIT + LIMIT FIX |
| `Validation.Url.MaxLength` | `SystemSettings.LogoUrlDarkTooLong` | `#/logoUrlDark` | SPLIT + LIMIT FIX |
| `Validation.Url.MaxLength` | `SystemSettings.FaviconUrlTooLong` | `#/faviconUrl` | SPLIT + LIMIT FIX |
| `Validation.SectionKey.Required` | `SystemSettings.SectionKeyRequired` | — |  |
| `Validation.SectionKey.MaxLength` | `SystemSettings.SectionKeyTooLong` | — |  |
| `Validation.RefreshToken.Required` | `Auth.RefreshTokenRequired` | `#/refreshToken` |  |
| `Validation.Token.Required` | `Auth.TokenRequired` | `#/token` | SPLIT: token revocation |
| `Validation.ResetToken.Required` | `PasswordReset.TokenRequired` | `#/token` |  |
| `Validation.TotpCode.Required` | `TwoFactor.CodeRequired` | `#/code` | SPLIT: 2FA endpoints |
| `Validation.TotpCode.InvalidFormat` | `TwoFactor.CodeInvalidFormat` | `#/code` | SPLIT: 2FA endpoints |
| `Validation.TotpCode.Required` | `EmailVerification.OtpRequired` | `#/otp` | SPLIT: registration and email verification |
| `Validation.TotpCode.InvalidFormat` | `EmailVerification.InvalidOtpFormat` | `#/otp` | EXISTING code reused (it was unreachable through the API) |
| `(unkeyed) VerifyEmail UserId-or-Email` | `EmailVerification.TargetRequired` | — | NEW: rule had plain English |
| `Validation.RecoveryCode.Required` | `TwoFactor.RecoveryCodeRequired` | `#/code` |  |
| `Validation.TwoFactorChallengeToken.Required` | `TwoFactor.ChallengeTokenRequired` | `#/challengeToken` |  |
| `Validation.IdToken.Required` | `ExternalAuth.IdTokenRequired` | `#/idToken` |  |
| `Validation.Provider.Required` | `ExternalAuth.ProviderRequired` | `#/provider` |  |
| `Validation.Provider.TooLong` | `ExternalAuth.ProviderTooLong` | `#/provider` |  |
| `Validation.OtpCode.Required` | `AccountDeletion.OtpCodeRequired` | `#/otpCode` | SPLIT by field |
| `Validation.OtpCode.InvalidFormat` | `AccountDeletion.OtpCodeInvalidFormat` | `#/otpCode` | SPLIT by field |
| `Validation.OtpCode.Required` | `Secret.ChallengeCodeRequired` | `#/code` | SPLIT by field |
| `Validation.OtpCode.InvalidFormat` | `Secret.ChallengeCodeInvalidFormat` | `#/code` | SPLIT by field |
| `Validation.SecretKey.Required` | `Secret.KeyRequired` | — |  |
| `Validation.SecretKey.MaxLength` | `Secret.KeyTooLong` | — |  |
| `Validation.SecretKey.InvalidFormat` | `Secret.KeyInvalidFormat` | — |  |
| `Validation.SecretValue.Required` | `Secret.ValueRequired` | `#/value` |  |
| `Validation.SecretValue.MaxLength` | `Secret.ValueTooLong` | `#/value` |  |
| `Validation.SecretOperation.Invalid` | `Secret.OperationInvalid` | `#/operation` |  |
| `Validation.SecretOperation.ValueRequired` | `Secret.OperationValueRequired` | `#/value` |  |
| `Validation.SecretOperation.ChallengeRequired` | `Secret.ChallengeIdRequired` | `#/challengeId` |  |
| `Validation.GatewayToken.Required` | `Secret.GatewayTokenRequired` | — |  |
| `Validation.GatewayToken.MinLength` | `Secret.GatewayTokenTooShort` | — |  |
| `Validation.HmacKey.Required` | `Secret.HmacKeyRequired` | — |  |
| `Validation.RsaPrivateKey.Required` | `Secret.RsaPrivateKeyRequired` | — |  |
| `Validation.AuditParticipant.IdRequired` | `AuditLog.ParticipantIdRequired` | `#/participantId` |  |
| `Validation.AuditParticipant.RoleRequired` | `AuditLog.ParticipantRoleRequired` | `#/participantRole` |  |
| `Validation.DateRange.Invalid` | `AuditLog.DateRangeInvalid` | `#/toDate` |  |
| `Validation.EntityType.Required` | `AuditLog.EntityTypeRequired` | — |  |
| `Validation.EntityType.MaxLength` | `AuditLog.EntityTypeTooLong` | — |  |
| `Validation.ExportFormat.Required` | `AuditLog.ExportFormatRequired` | `#/format` |  |
| `Validation.ExportFormat.Invalid` | `AuditLog.ExportFormatInvalid` | `#/format` |  |
| `Validation.MaxRecords.Range` | `AuditLog.ExportMaxRecordsOutOfRange` | `#/maxRecords` |  |
| `Validation.PolicyVersion.Required` | `PrivacyPolicy.VersionRequired` | `#/version` |  |
| `Validation.PolicyVersion.InvalidFormat` | `PrivacyPolicy.VersionInvalidFormat` | `#/version` |  |
| `(unkeyed) SetMyUiPreference Key` | `UiPreference.KeyRequired` | `#/key` | NEW |
| `(unkeyed) SetMyUiPreference Key` | `UiPreference.KeyTooLong` | `#/key` | NEW |
| `(unkeyed) SetMyUiPreference Key` | `UiPreference.KeyInvalidFormat` | `#/key` | NEW: rule had plain English |
| `(unkeyed) SetMyUiPreference Value` | `UiPreference.ValueRequired` | `#/value` | NEW |
| `(unkeyed) SetMyUiPreference Value` | `UiPreference.ValueTooLong` | `#/value` | NEW |
| `Validation.NotificationTypeId.Required` | `Notification.TypeIdRequired` | `#/notificationTypeId` |  |
| `Validation.NotificationChannel.Invalid` | `Notification.ChannelInvalid` | `#/channel` |  |
| `Validation.NotificationLanguage.Required` | `Notification.DefaultLanguageRequired` | `#/defaultLanguage` | SPLIT by field |
| `Validation.NotificationLanguage.NotSupported` | `Notification.DefaultLanguageNotSupported` | `#/defaultLanguage` | SPLIT by field |
| `Validation.NotificationLanguage.Required` | `Notification.LanguageCodeRequired` | `#/languageCode` | SPLIT by field |
| `Validation.NotificationLanguage.NotSupported` | `Notification.LanguageCodeNotSupported` | `#/languageCode` | SPLIT by field |
| `Validation.NotificationLanguage.Required` | `Notification.TranslationLanguageRequired` | `#/translations` | SPLIT; nested: exact path in errors[].pointer |
| `Validation.NotificationLanguage.NotSupported` | `Notification.TranslationLanguageNotSupported` | `#/translations` | SPLIT; nested |
| `Validation.NotificationLanguage.NotSupported` | `Notification.RemovedLanguageNotSupported` | `#/removeLanguages` | SPLIT |
| `Validation.NotificationSubject.Required` | `Notification.SubjectRequired` | — | nested in translations: no single pointer |
| `Validation.NotificationSubject.MaxLength` | `Notification.SubjectTooLong` | — | top-level in preview, nested in draft |
| `Validation.NotificationBody.Required` | `Notification.BodyHtmlRequired` | — | nested |
| `Validation.NotificationBody.MaxLength` | `Notification.BodyHtmlTooLong` | — | SPLIT by field |
| `Validation.NotificationBody.MaxLength` | `Notification.BodyTextTooLong` | — | SPLIT by field |
| `Validation.NotificationChangeNote.MaxLength` | `Notification.ChangeNoteTooLong` | `#/changeNote` |  |
| `Validation.NotificationRecipientEmail.Required` | `Notification.RecipientEmailRequired` | `#/recipientEmail` |  |
| `Validation.NotificationRecipientEmail.InvalidFormat` | `Notification.RecipientEmailInvalidFormat` | `#/recipientEmail` |  |
| `Validation.NotificationLayoutName.Required` | `Notification.LayoutNameRequired` | `#/name` |  |
| `Validation.NotificationLayoutName.MaxLength` | `Notification.LayoutNameTooLong` | `#/name` |  |
| `Validation.NotificationLayoutContent.Required` | `Notification.LayoutContentRequired` | `#/draftContent` | EXISTING code reused (NotificationLayout raises it for the same rule) |
| `Validation.NotificationLayoutContent.MaxLength` | `Notification.LayoutContentTooLong` | `#/draftContent` | SPLIT by field |
| `Validation.NotificationLayoutContent.Required` | `Notification.PreviewLayoutContentRequired` | `#/layoutContent` | SPLIT by field (preview) |
| `Validation.NotificationLayoutContent.MaxLength` | `Notification.PreviewLayoutContentTooLong` | `#/layoutContent` | SPLIT by field (preview) |
| `Validation.NotificationLayoutStrings.InvalidJson` | `Notification.LayoutStringsInvalidJson` | `#/draftStringsJson` | SPLIT by field |
| `Validation.NotificationLayoutStrings.InvalidJson` | `Notification.PreviewLayoutStringsInvalidJson` | `#/layoutStringsJson` | SPLIT by field (preview) |
| `Validation.NotificationTypeName.Required` | `Notification.TypeNameRequired` | `#/name` |  |
| `Validation.NotificationTypeName.MaxLength` | `Notification.TypeNameTooLong` | `#/name` |  |
| `Validation.NotificationTypeDescription.MaxLength` | `Notification.TypeDescriptionTooLong` | `#/description` |  |
| `Validation.NotificationSampleData.InvalidJson` | `Notification.SampleDataInvalidJson` | `#/sampleDataJson` |  |
| `Validation.NotificationVariables.InvalidJson` | `Notification.VariablesInvalidJson` | `#/variablesJson` |  |
