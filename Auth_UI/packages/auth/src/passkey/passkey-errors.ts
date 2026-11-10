import { getErrorCodes, getErrorMessage, getErrorStatus } from "@authsystem/api/errors"
import { retryAfterSeconds } from "@authsystem/api/retry-after"
import i18n from "@authsystem/i18n"

import { isCeremonyCancelled } from "./webauthn"

/**
 * What a passkey page says when a request or a ceremony failed (S20 PR C).
 *
 * The API's catalog sentence when it answered one; the way out for a passkey that
 * was removed from the account but is still on the device (D-S20-7); the wait a
 * 429 asked for, from Retry-After, the only place it travels (ARR-0668); and
 * nothing at all when the person cancelled, or the page started another
 * ceremony (an abort is not a failure).
 */

/** The wait a 429 asked for, carried on the failure a passkey call throws. */
interface WithRetryAfter {
  retryAfterSeconds?: number | null
}

/**
 * The failure of a passkey call, with the wait of a 429 kept on it: the problem
 * body names no wait, and the caller only sees the body.
 */
export function withRetryAfter(error: unknown, response: Response | undefined): unknown {
  if (response?.status === 429 && error && typeof error === "object") {
    return Object.assign(error, { retryAfterSeconds: retryAfterSeconds(response) } satisfies WithRetryAfter)
  }
  return error
}

function retryAfterOf(error: unknown): number | null {
  const seconds = (error as WithRetryAfter | null)?.retryAfterSeconds
  return typeof seconds === "number" && seconds > 0 ? seconds : null
}

/** A failure the browser raised, not the server: the ceremony itself was refused. */
function isCeremonyError(error: unknown): boolean {
  if (!error || typeof error !== "object") return false
  if (getErrorStatus(error) !== undefined) return false
  const { name, code } = error as { name?: unknown; code?: unknown }
  return (
    typeof code === "string" ||
    name === "SecurityError" ||
    name === "NotSupportedError" ||
    name === "InvalidStateError" ||
    name === "ConstraintError" ||
    name === "UnknownError"
  )
}

/**
 * The sentence for a failed passkey sign-in, its options, or a recovery with a
 * passkey; null when there is nothing to say (the ceremony was cancelled).
 */
export function passkeyFailureMessage(error: unknown): string | null {
  if (isCeremonyCancelled(error)) return null

  const status = getErrorStatus(error)
  if (status === 429) {
    const seconds = retryAfterOf(error)
    return seconds
      ? i18n.t("auth.passkey.rateLimitedFor", { seconds })
      : i18n.t("auth.passkey.rateLimited")
  }
  // Passkeys were switched off after the page fetched its options: only the
  // options' own 404 hides the button (T-C4); any other is said as it is.
  if (status === 404) return i18n.t("auth.passkey.unavailableNow")

  if (getErrorCodes(error).includes("Passkey.VerificationFailed")) {
    return `${getErrorMessage(error)} ${i18n.t("auth.passkey.removedPasskeyWayOut")}`
  }
  if (isCeremonyError(error)) return i18n.t("auth.passkey.deviceRefused")
  return getErrorMessage(error)
}

/**
 * The sentence for a sign-in ceremony that ended without an answer: nothing when
 * it was cancelled, else that this device or browser refused. Every rejection of
 * the ceremony is the browser's, whatever its name: the helper passes the names
 * it does not know through as they are (an `OperationError`), and throws a plain
 * Error when the browser answered with no credential. None of them is the
 * server's unknown fault, whose sentence sends the person to support (seen on a
 * device check, 2026-11-10: the browser refused the press without asking the
 * platform's authenticator).
 */
export function ceremonyFailureMessage(error: unknown): string | null {
  return isCeremonyCancelled(error) ? null : i18n.t("auth.passkey.deviceRefused")
}

/**
 * The sentence for a failed passkey proof inside the "Confirm it's you" window, its options
 * included: the person is signed in, so nothing tells them to sign in another way
 * (REVIEW-FIXES 152 NIT-6, the twin of the adding dialog's own sentence). Where the sign-in's
 * sentence would, after a refusal of the device or the browser, a passkey that did not verify,
 * or passkeys switched off meanwhile, the window's sentence points to the other ways below it.
 * A cancel says nothing; a wait (429) and another account's passkey keep their own sentences.
 */
export function proofFailureMessage(error: unknown): string | null {
  if (isCeremonyCancelled(error)) return null

  const status = getErrorStatus(error)
  const codes = getErrorCodes(error)
  if (status === 429 || codes.includes("Passkey.NotThisAccount")) return passkeyFailureMessage(error)
  if (status === undefined || status === 404 || codes.includes("Passkey.VerificationFailed")) {
    return i18n.t("auth.reauthenticate.passkeyFailed")
  }
  return getErrorMessage(error)
}

/** Whether the failure is worth another try by itself (a server or network fault). */
export function isPasskeyFailureRetryable(error: unknown): boolean {
  if (isCeremonyCancelled(error)) return false
  const status = getErrorStatus(error)
  if (status === undefined) return !isCeremonyError(error)
  return status >= 500
}

/** When a failed options request may be tried again by itself, in ms: the 429's wait, else a minute. */
export function retryDelayMs(error: unknown): number {
  return (retryAfterOf(error) ?? 60) * 1000
}
