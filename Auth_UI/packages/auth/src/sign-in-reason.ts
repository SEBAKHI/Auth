/**
 * Why the sign-in page is shown again, when an application's two-step
 * requirement sent the person back to it (S23), and the one module that knows
 * the words.
 *
 * The authorize endpoint adds two short words to the accounts URL, after the
 * pending request: `reason`, what the application still needs, and `method`,
 * the way the account signs in first (D-S23-3 (c): `password` for an account
 * that has one, `code` for one that signs in by the emailed code only).
 *
 * Display hints, nothing more. The server decides again from the session when
 * the request resumes, so a forged or dropped word changes what the page says
 * and which method it puts first, never what the server requires. Like the
 * pending request, the pair travels in the accounts links' query, in router
 * state and in the in-memory challenge store, never in browser storage, and it
 * never goes back to the server: the resume URL carries only the handle.
 *
 * Four reasons are read. `mfa_passkey` (S20): the account's only qualifying
 * second factor is a passkey, so the page offers the passkey first, and names
 * the way out for a lost one. Any other word, and a method word without a
 * reason, are ignored, silently.
 */

export const SIGN_IN_REASONS = [
  "mfa_step_up",
  "mfa_enroll",
  "mfa_reauthenticate",
  "mfa_passkey",
] as const

export type SignInReasonWord = (typeof SIGN_IN_REASONS)[number]

/** The method the server names for the account. */
export type SignInReasonMethod = "password" | "code"

export interface SignInReason {
  reason: SignInReasonWord
  /** The method the page puts first; null when none, or an unknown one, came. */
  method: SignInReasonMethod | null
}

const REASON_PARAM = "reason"
const METHOD_PARAM = "method"

function isReasonWord(value: unknown): value is SignInReasonWord {
  return (
    typeof value === "string" &&
    (SIGN_IN_REASONS as readonly string[]).includes(value)
  )
}

function isMethodWord(value: unknown): value is SignInReasonMethod {
  return value === "password" || value === "code"
}

/**
 * The pair as router state or the in-memory store holds it, validated by the
 * same rule as the query: re-checked at every hop, never trusted.
 */
export function validateSignInReason(value: unknown): SignInReason | null {
  if (!value || typeof value !== "object") return null
  const { reason, method } = value as Record<string, unknown>
  if (!isReasonWord(reason)) return null
  return { reason, method: isMethodWord(method) ? method : null }
}

/** The pair from an accounts page's query string. */
export function readSignInReason(search: string): SignInReason | null {
  const params = new URLSearchParams(search)
  return validateSignInReason({
    reason: params.get(REASON_PARAM),
    method: params.get(METHOD_PARAM),
  })
}

/**
 * The pair as query words to follow the pending request (`&reason=…&method=…`),
 * or nothing. Only `toAccountsSearch` writes it, after a valid request.
 */
export function signInReasonQuery(
  reason: SignInReason | null | undefined
): string {
  const valid = validateSignInReason(reason)
  if (!valid) return ""
  const method = valid.method ? `&${METHOD_PARAM}=${valid.method}` : ""
  return `&${REASON_PARAM}=${valid.reason}${method}`
}
