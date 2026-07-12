/**
 * Normalizes API error payloads into a human-readable message.
 *
 * The Auth API returns RFC 7807 ProblemDetails. Depending on the failure it may
 * carry either an ErrorOr-style `errors: [{ code, description }]` array or an
 * ASP.NET validation `errors: { field: string[] }` dictionary.
 */
import i18n from "@astoom/i18n"

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

function flattenErrors(
  errors: ErrorOrEntry[] | Record<string, string[]>
): string[] {
  if (Array.isArray(errors)) {
    return errors
      .map((e) => e.description ?? e.code ?? "")
      .filter((m): m is string => m.length > 0)
  }
  return Object.values(errors).flat().filter(Boolean)
}

/**
 * Extract a display message from an unknown error/ProblemDetails body.
 * `error` is whatever openapi-fetch returned in its `error` slot, or a thrown value.
 */
export function getErrorMessage(
  error: unknown,
  // Default parameter expressions run per call, so language switches apply.
  fallback = i18n.t("errors.generic")
): string {
  if (!error) return fallback

  if (typeof error === "string") return error

  if (error instanceof Error) return error.message || fallback

  if (typeof error === "object") {
    const body = error as ApiErrorBody
    if (body.errors) {
      const messages = flattenErrors(body.errors)
      if (messages.length > 0) return messages.join(" ")
    }
    if (body.detail) return body.detail
    if (body.title) return body.title
  }

  return fallback
}

/** Extract the ErrorOr error codes (e.g. "User.EmailNotConfirmed") from an API error. */
export function getErrorCodes(error: unknown): string[] {
  if (!error || typeof error !== "object") return []
  const body = error as ApiErrorBody
  const codes: string[] = []
  if (Array.isArray(body.errors)) {
    for (const e of body.errors) {
      if (e.code) codes.push(e.code)
    }
  }
  // Single-error ProblemDetails carry the ErrorOr code in `title`.
  if (codes.length === 0 && body.title && !body.title.includes(" ")) {
    codes.push(body.title)
  }
  return codes
}

/** Map a ProblemDetails `errors` array/dictionary to per-field messages. */
export function getFieldErrors(error: unknown): Record<string, string> {
  const result: Record<string, string> = {}
  if (!error || typeof error !== "object") return result

  const errors = (error as ApiErrorBody).errors
  if (!errors || Array.isArray(errors)) return result

  for (const [field, messages] of Object.entries(errors)) {
    if (messages.length > 0) {
      // Normalize PascalCase field names from the API to camelCase form names.
      const key = field.charAt(0).toLowerCase() + field.slice(1)
      result[key] = messages[0]
    }
  }
  return result
}
