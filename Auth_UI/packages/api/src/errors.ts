/**
 * The one reader of Auth API failures (ADR 0001), and the only module that
 * looks inside an error body.
 *
 * Every error the API and its gateway send is `application/problem+json` whose
 * `code` is always present and always one of the published codes
 * (`error-codes.generated.ts`, loaded on the first failure: `published-codes.ts`).
 * The client middleware passes every failed response through {@link readProblem},
 * so what a query or mutation throws carries the TRANSPORT status even when the
 * body was empty or not JSON - a proxy's 502, an HTML error page - and is judged
 * with the code map in hand. From there:
 *
 * - the kind of failure comes from the code when one is known here, else from
 *   the transport status;
 * - the code is `code`, checked against the published map. `title` is a human
 *   summary in the framework's words and is never read;
 * - `detail` is the API's own sentence for that code, localized by its catalog
 *   into all seven languages and often carrying a fact this client cannot
 *   reconstruct (a deletion deadline, a lock-out expiry, a minimum length). It
 *   is shown only for catalog codes whose sentence carries nothing the backend
 *   did not author ({@link OPAQUE_DETAIL_CODES});
 * - fields come from `errors[].pointer` when the API reports several failures,
 *   else from the published pointer of the one code.
 *
 * Exception text is never on the wire, and `Error.message` is never rendered.
 */
import i18n from "@authsystem/i18n"
import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"
import {
  isPublishedCode,
  loadPublishedErrorCodes,
  publishedOrigin,
} from "@authsystem/api/published-codes"

/** One failure of a Validation result with several: its code, and the body member it concerns. */
interface ProblemEntry {
  code?: string
  pointer?: string
}

/**
 * A failed response as the rest of the client sees it: the problem body, with
 * `status` set to the transport's status by {@link readProblem}.
 */
export interface ApiProblem {
  status: number
  code?: string
  detail?: string
  errors?: ProblemEntry[]
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
  codes: PublishedErrorCode[]
}

/**
 * Reads a failed response into an {@link ApiProblem}. The status is always the
 * transport's, whatever the body says or whether there is one: a 401 or 403
 * from the authentication layer and a 502 or 504 from a proxy still classify by
 * what actually happened.
 */
export async function readProblem(response: Response): Promise<ApiProblem> {
  await loadPublishedErrorCodes()

  let body: unknown
  try {
    body = await response.json()
  } catch {
    body = undefined
  }

  const problem =
    body && typeof body === "object" && !Array.isArray(body) ? body : {}
  return { ...problem, status: response.status }
}

const CODE_KINDS = {
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
} satisfies Partial<Record<PublishedErrorCode, ApiErrorKind>>

const RETRYABLE_KINDS = new Set<ApiErrorKind>(["server", "network"])

/**
 * Catalog codes whose sentence interpolates text the backend did not author,
 * so the localized wrapper is localized but its payload is not:
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
const OPAQUE_DETAIL_CODES = new Set<PublishedErrorCode>([
  "Secret.ConnectionStringUnreachable",
  "Secret.ConnectionStringMalformed",
  "Notification.RenderFailed",
])

function asProblem(error: unknown): Partial<ApiProblem> | undefined {
  return error && typeof error === "object" ? (error as Partial<ApiProblem>) : undefined
}

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
 * The API's own sentence for the problem's code, when it is safe to show: the
 * code is published, belongs to the catalog (a transport code's sentence is
 * generic, and the local copy for its kind says what to do next), and is not
 * opaque. `detail` always describes `code`, so no other entry has one.
 */
function catalogDetail(error: unknown): { code: PublishedErrorCode; description: string } | undefined {
  const problem = asProblem(error)
  const code = problem?.code
  if (!isPublishedCode(code)) return undefined

  const origin = publishedOrigin(code)
  if (origin === "transport" || origin === "challenge") return undefined
  if (OPAQUE_DETAIL_CODES.has(code)) return undefined

  const description = typeof problem?.detail === "string" ? problem.detail.trim() : ""
  return description ? { code, description } : undefined
}

function classifyError(
  error: unknown,
  codes: readonly PublishedErrorCode[],
  status: number | undefined
): ApiErrorKind {
  for (const code of codes) {
    if (Object.hasOwn(CODE_KINDS, code)) {
      return CODE_KINDS[code as keyof typeof CODE_KINDS]
    }
  }
  return (
    kindFromStatus(status) ?? (isNetworkFailure(error) ? "network" : "unknown")
  )
}

/**
 * Return stable, localized feedback for an unknown thrown value or problem.
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
      catalogDetail(error)?.description ?? i18n.t(`errors.feedback.${kind}`),
    actionLabel: i18n.t("errors.feedback.retry"),
    retryable: RETRYABLE_KINDS.has(kind),
    status,
    codes,
  }
}

/**
 * The one sentence for a text-only error surface (a toast): the API's catalog
 * sentence when it is safe, else local copy that includes a recovery step.
 */
export function getErrorMessage(
  error: unknown,
  fallback = i18n.t("errors.feedback.unknown")
): string {
  return getErrorFeedback(error).description || fallback
}

/**
 * The transport status of a failed request, or undefined when the request did
 * not yield a server response (a network failure, a thrown exception).
 */
export function getErrorStatus(error: unknown): number | undefined {
  const status = asProblem(error)?.status
  return typeof status === "number" ? status : undefined
}

/**
 * The published codes of a failure, the problem's own `code` first and then any
 * further ones its `errors` list. A code this client does not know is left out,
 * so it classifies by status rather than by a guess.
 */
export function getErrorCodes(error: unknown): PublishedErrorCode[] {
  const problem = asProblem(error)
  if (!problem) return []

  const codes: PublishedErrorCode[] = []
  const add = (code: unknown) => {
    if (isPublishedCode(code) && !codes.includes(code)) codes.push(code)
  }

  add(problem.code)
  if (Array.isArray(problem.errors)) {
    for (const entry of problem.errors) add(entry?.code)
  }
  return codes
}

/**
 * The form path a pointer names: `#/translations/0/languageCode` is
 * `translations.0.languageCode`, the react-hook-form spelling. Undefined for
 * anything that is not a pointer into the body.
 */
function fieldFromPointer(pointer: string): string | undefined {
  if (!pointer.startsWith("#/")) return undefined

  const segments = pointer
    .slice(2)
    .split("/")
    .map((segment) =>
      decodeURIComponent(segment).replaceAll("~1", "/").replaceAll("~0", "~")
    )
  return segments.every(Boolean) ? segments.join(".") : undefined
}

/**
 * Which fields the server rejected, each with localized copy.
 *
 * A Validation result with several failures lists each one's pointer in
 * `errors`; a single failure carries only its code, whose published pointer
 * names the field. Only the field is taken from the server - its text is not -
 * and owning forms must still allow-list the fields they own, because a pointer
 * into the body is not proof that the form has a control by that name.
 */
export function getFieldErrors(error: unknown): Record<string, string> {
  const problem = asProblem(error)
  if (!problem) return {}

  const pointers: string[] = []
  if (Array.isArray(problem.errors) && problem.errors.length > 0) {
    for (const entry of problem.errors) {
      if (typeof entry?.pointer === "string") pointers.push(entry.pointer)
    }
  } else {
    const origin = publishedOrigin(problem.code)
    if (origin?.startsWith("#/")) pointers.push(origin)
  }

  const result: Record<string, string> = {}
  for (const pointer of pointers) {
    const field = fieldFromPointer(pointer)
    if (field && !(field in result)) {
      result[field] = i18n.t("errors.feedback.fieldInvalid")
    }
  }
  return result
}

/**
 * The API's own localized sentence for a failure, with the code it describes,
 * when it is safe to show - the same trust boundary as {@link getErrorMessage}.
 * The API writes one sentence per response, for its `code`; the other failures
 * of a Validation result carry only their codes, and their copy is local.
 */
export function getErrorDetail(
  error: unknown
): { code: PublishedErrorCode; description: string } | undefined {
  return catalogDetail(error)
}
