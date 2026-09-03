/**
 * Normalizes Auth API failures without exposing backend exception text.
 *
 * The API returns RFC 7807 ProblemDetails whose `title` carries an ErrorOr
 * code, with the remaining codes in an `errors` array when a request failed
 * more than one rule. The shape of that code says where `detail` came from,
 * and that is the whole basis of this module:
 *
 * - A **dotted** code (`User.AccountPendingDeletion`) addressed the backend's
 *   DomainErrors catalog, so `detail` is a sentence that catalog localized into
 *   all seven languages - often carrying a fact this client cannot reconstruct,
 *   such as a deletion deadline or a lock-out expiry. It is preferred over local
 *   copy. A backend test (DomainErrorResourceCoverageTests) fails the build if
 *   any domain code lacks an entry, and another (BaselineCoverageTests) fails it
 *   if any culture is missing a key, which is what makes this safe.
 * - A **bare** code (`PhoneNumber`) is a FluentValidation property name. Its
 *   `detail` is only localized when the rule opted into a resource key, so it
 *   may be raw English. It is never rendered - it is read as the name of the
 *   field to highlight.
 *
 * Exception text, `Error.message`, and untranslated resource keys are never
 * rendered as user-facing copy on any path.
 */
import i18n from "@authsystem/i18n"

interface ErrorOrEntry {
  code?: string
  description?: string
}

interface ApiErrorBody {
  title?: string | null
  detail?: string | null
  status?: number | null
  errors?: ErrorOrEntry[] | Record<string, string[]> | null
}

export type ApiErrorKind =
  | "validation"
  | "authentication"
  | "authorization"
  | "notFound"
  | "conflict"
  | "rateLimit"
  | "server"
  | "network"
  | "unknown"
  | "duplicateEmail"
  | "invalidCredentials"
  | "pendingDeletion"
  | "staleData"
  | "invalidChallengeCode"
  | "connectionUnreachable"

export interface ApiErrorFeedback {
  kind: ApiErrorKind
  title: string
  description: string
  actionLabel: string
  retryable: boolean
  status?: number
  codes: string[]
}

const CODE_KINDS: Readonly<Record<string, ApiErrorKind>> = {
  "User.DuplicateEmail": "duplicateEmail",
  "User.InvalidCredentials": "invalidCredentials",
  "User.AccountPendingDeletion": "pendingDeletion",
  "Notification.ConcurrencyConflict": "staleData",
  "Notification.PublishTargetChanged": "staleData",
  "Notification.UnpublishTargetChanged": "staleData",
  "Notification.LayoutPublishTargetChanged": "staleData",
  "SystemSettings.ConcurrencyConflict": "staleData",
  "Secret.InvalidChallengeCode": "invalidChallengeCode",
  "Secret.ConnectionStringUnreachable": "connectionUnreachable",
}

const RETRYABLE_KINDS = new Set<ApiErrorKind>(["server", "network"])

function isNetworkFailure(error: unknown): boolean {
  if (error instanceof TypeError) return true
  return Boolean(
    error &&
    typeof error === "object" &&
    "name" in error &&
    (error as { name?: unknown }).name === "TypeError"
  )
}

function kindFromStatus(status: number | undefined): ApiErrorKind | undefined {
  if (status === 400 || status === 422) return "validation"
  if (status === 401) return "authentication"
  if (status === 403) return "authorization"
  if (status === 404) return "notFound"
  if (status === 409) return "conflict"
  if (status === 429) return "rateLimit"
  if (status !== undefined && status >= 500) return "server"
  return undefined
}

/**
 * The namespaces the backend's DomainErrors catalog is written in - one per
 * error class in `Auth.Domain/Errors`.
 *
 * An allow-list, not "does it contain a dot". A dot is not proof of a domain
 * error: an unhandled 500 arrives with an exception type in `title`
 * ("System.DatabaseUnavailableException") and the exception's own message in
 * `detail`, and that heuristic would have rendered the message. Failing closed
 * costs a specific sentence for a code nobody has registered here; failing open
 * costs a stack trace in front of an administrator.
 *
 * `domain-error-namespaces.test.ts` holds this list to those classes.
 */
const DOMAIN_ERROR_NAMESPACES = new Set([
  "AccountDeletion",
  "ApiKey",
  "Application",
  "AuditLog",
  "Auth",
  "Device",
  "EmailVerification",
  "ExternalAuth",
  "Image",
  "Notification",
  "Organization",
  // Not an `Auth.Domain/Errors` class: the password policy is enforced in
  // `Auth.Application/Validators/PasswordValidator.cs`, which raises the same
  // shape (`Error.Validation("Password.TooShort", "Validation.Password.…")`)
  // and is localized by the same catalog. Leaving it out suppressed the one
  // sentence that tells a person WHICH rule their password broke.
  "Password",
  "PasswordReset",
  "Permission",
  "PrivacyPolicy",
  "Role",
  "Secret",
  "Session",
  "SystemSettings",
  "TwoFactor",
  "UiPreference",
  "User",
  "WebhookKey",
])

/**
 * A code the DomainErrors catalog answers to, as opposed to a validation
 * property name (`Error.Validation(code: f.PropertyName, ...)` never contains a
 * dot) or an exception type that leaked into `title`.
 */
function isDomainCode(code: string): boolean {
  const namespace = code.slice(0, code.indexOf("."))
  return namespace.length > 0 && DOMAIN_ERROR_NAMESPACES.has(namespace)
}

/**
 * Domain codes whose catalog sentence interpolates text the backend did not
 * author, so the localized wrapper is localized but its payload is not:
 *
 * - `Secret.ConnectionString*` embed the driver's own exception message
 *   (`SqlConnectionStringProbe` passes `ex.Message` straight through), which
 *   names the host, the instance and the login it tried.
 * - `Notification.RenderFailed` embeds the Liquid/JSON parser's message.
 *
 * These keep local copy. The list is deliberately explicit rather than a
 * heuristic on the text: a sentence carrying an exception is indistinguishable
 * from a well-written one by inspection, and guessing wrong here leaks
 * infrastructure detail into a screen a non-technical admin is reading.
 */
const OPAQUE_DETAIL_CODES = new Set([
  "Secret.ConnectionStringUnreachable",
  "Secret.ConnectionStringMalformed",
  "Notification.RenderFailed",
])

/**
 * The server's own localized sentence, when it is safe to show. Only domain
 * codes qualify; an empty detail, one that is just the code echoed back, or one
 * belonging to a code above falls through to local copy.
 */
function localizedDetail(
  error: unknown,
  codes: readonly string[]
): string | undefined {
  if (!codes.some(isDomainCode)) return undefined
  if (codes.some((code) => OPAQUE_DETAIL_CODES.has(code))) return undefined
  if (!error || typeof error !== "object") return undefined
  const detail = (error as ApiErrorBody).detail
  if (typeof detail !== "string") return undefined
  const trimmed = detail.trim()
  if (!trimmed || codes.includes(trimmed)) return undefined
  return trimmed
}

function classifyError(
  error: unknown,
  codes: readonly string[],
  status: number | undefined
): ApiErrorKind {
  for (const code of codes) {
    const kind = CODE_KINDS[code]
    if (kind) return kind
  }
  return (
    kindFromStatus(status) ?? (isNetworkFailure(error) ? "network" : "unknown")
  )
}

/**
 * Return stable, localized feedback for an unknown thrown value or ProblemDetails.
 * Error codes take precedence over HTTP status so known recovery flows can remain
 * specific while unknown codes still degrade safely to their status category.
 */
export function getErrorFeedback(error: unknown): ApiErrorFeedback {
  const status = getErrorStatus(error)
  const codes = getErrorCodes(error)
  const kind = classifyError(error, codes, status)

  return {
    kind,
    title: i18n.t("errors.feedback.title"),
    description:
      localizedDetail(error, codes) ?? i18n.t(`errors.feedback.${kind}`),
    actionLabel: i18n.t("errors.feedback.retry"),
    retryable: RETRYABLE_KINDS.has(kind),
    status,
    codes,
  }
}

/**
 * Compatibility helper for existing text-only error surfaces. The returned
 * sentence is always local copy and includes a concrete recovery step.
 */
export function getErrorMessage(
  error: unknown,
  fallback = i18n.t("errors.feedback.unknown")
): string {
  return getErrorFeedback(error).description || fallback
}

/**
 * The HTTP status carried by ProblemDetails, or undefined when the request did
 * not yield a typed server response.
 */
export function getErrorStatus(error: unknown): number | undefined {
  if (!error || typeof error !== "object") return undefined
  const { status } = error as ApiErrorBody
  return typeof status === "number" ? status : undefined
}

/** Extract ErrorOr codes such as `User.EmailNotConfirmed` from ProblemDetails. */
export function getErrorCodes(error: unknown): string[] {
  if (!error || typeof error !== "object") return []
  const body = error as ApiErrorBody
  const codes: string[] = []
  if (Array.isArray(body.errors)) {
    for (const entry of body.errors) {
      if (entry.code) codes.push(entry.code)
    }
  }
  // Single-error ProblemDetails carry the ErrorOr code in `title`.
  if (codes.length === 0 && body.title && !body.title.includes(" ")) {
    codes.push(body.title)
  }
  return codes
}

function camelCase(field: string): string {
  return field.charAt(0).toLowerCase() + field.slice(1)
}

/**
 * Which fields the server rejected, each with localized copy.
 *
 * Two payload shapes reach here and both matter. Handler validation runs through
 * FluentValidation, which names the offending property as the ErrorOr code, so
 * the field arrives in `title` (one failure) or in `errors[].code` (several).
 * Request DTOs carrying DataAnnotations fail earlier, in ASP.NET's model binder,
 * which sends the familiar `{ field: [message] }` dictionary instead.
 *
 * Backend field text is deliberately ignored in both: it may be jargon, an
 * untranslated resource key, or implementation detail. Only the field NAME is
 * taken from the server. Owning forms must still allow-list the names, because
 * a property name is not proof that the form owns a control by that name.
 */
export function getFieldErrors(error: unknown): Record<string, string> {
  const result: Record<string, string> = {}
  if (!error || typeof error !== "object") return result

  const errors = (error as ApiErrorBody).errors
  if (errors && !Array.isArray(errors)) {
    for (const [field, messages] of Object.entries(errors)) {
      if (Array.isArray(messages) && messages.length > 0) {
        result[camelCase(field)] = i18n.t("errors.feedback.fieldInvalid")
      }
    }
    return result
  }

  for (const code of getErrorCodes(error)) {
    // A dotted code is a domain rule, not a field: nothing on the form is
    // called "User.DuplicateEmail", and treating it as a field name would
    // highlight nothing while swallowing the message the page should show.
    // Callers already fall back to an alert carrying the server's sentence
    // when no field matches, so there is nothing to rescue by guessing one.
    if (isDomainCode(code)) continue
    result[camelCase(code)] = i18n.t("errors.feedback.fieldInvalid")
  }
  return result
}

/**
 * Every localized sentence the backend attached to one failure, in the order
 * it listed them, each with the code that produced it.
 *
 * `getErrorMessage` deliberately collapses a multi-rule refusal to its first
 * sentence — right for a toast, wrong for a control whose rules the person has
 * to satisfy all at once. PasswordValidator reports every rule a password broke
 * in one response, and showing them one per submit is what made a sign-up feel
 * like an interrogation. Same trust boundary as `localizedDetail`: catalog
 * codes only, never an opaque one, never a bare validation property name.
 */
export function getErrorDescriptions(
  error: unknown
): Array<{ code: string; description: string }> {
  if (!error || typeof error !== "object") return []
  const body = error as ApiErrorBody

  if (Array.isArray(body.errors)) {
    const entries: Array<{ code: string; description: string }> = []
    for (const entry of body.errors) {
      if (!entry.code || !isDomainCode(entry.code)) continue
      if (OPAQUE_DETAIL_CODES.has(entry.code)) continue
      const description = entry.description?.trim()
      if (!description || description === entry.code) continue
      entries.push({ code: entry.code, description })
    }
    return entries
  }

  const codes = getErrorCodes(error)
  const detail = localizedDetail(error, codes)
  return detail && codes[0] ? [{ code: codes[0], description: detail }] : []
}
