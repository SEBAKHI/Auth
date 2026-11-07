import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"

/**
 * The answers of a recovery that cancelled the deletion and issued no session
 * (owner decision D-151-1 (b)): the person proved the recovery, so the account
 * will not be deleted, but it keeps the state it was in — deactivated, not yet
 * activated, or locked by an administrator — and cannot sign in.
 *
 * Not an error: the request did what it was asked. A page shows the sentence
 * as the outcome and offers the recovery no more, since a second press would
 * find nothing waiting for deletion and be answered "invalid credentials".
 */
export const DELETION_CANCELLED_CODES: readonly PublishedErrorCode[] = [
  "User.DeletionCancelledAccountInactive",
  "User.DeletionCancelledAccountPending",
  "User.DeletionCancelledAccountLocked",
]

/**
 * The outcome to show when `error` is one of {@link DELETION_CANCELLED_CODES}:
 * the API's own sentence for the code, which says both facts. Null for any
 * other failure, which the page handles as before.
 */
export function deletionCancelledOutcome(error: unknown): string | null {
  const codes = getErrorCodes(error)
  return DELETION_CANCELLED_CODES.some((code) => codes.includes(code))
    ? getErrorMessage(error)
    : null
}
