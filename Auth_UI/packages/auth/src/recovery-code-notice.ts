/**
 * A sign-in that spent a recovery code leaves one notice for the security page
 * (AM-S08-1): the codes are single-use, and an account that is down to its last
 * one with the phone gone has no way back but an administrator. The page shows
 * the "generate new codes" alert once, then clears the notice.
 *
 * Client state only, keyed by the account: a notice left for one account must
 * never show for the next one to sign in on this browser. Shared by the tabs of
 * the origin, so the security page opened in another tab still shows it. Storage
 * that is unavailable (a private window, a blocked origin) loses the notice and
 * nothing else — the count on the page says the same thing.
 */
const KEY_PREFIX = "auth.recoveryCodeSignIn:"

/** Records that this account just signed in with a recovery code. */
export function markRecoveryCodeSignIn(userId: string | null | undefined): void {
  if (!userId) return
  try {
    window.localStorage.setItem(KEY_PREFIX + userId, "1")
  } catch {
    /* storage unavailable: the notice is lost, nothing else */
  }
}

/**
 * Whether this account signed in with a recovery code since the notice was last
 * shown. Reads only, so it is safe in a render or a state initializer; the page
 * clears the notice once it has shown it ({@link clearRecoveryCodeSignIn}).
 */
export function hasRecoveryCodeSignIn(userId: string | null | undefined): boolean {
  if (!userId) return false
  try {
    return window.localStorage.getItem(KEY_PREFIX + userId) !== null
  } catch {
    return false
  }
}

/** Clears the notice, so it shows once. */
export function clearRecoveryCodeSignIn(userId: string | null | undefined): void {
  if (!userId) return
  try {
    window.localStorage.removeItem(KEY_PREFIX + userId)
  } catch {
    /* storage unavailable: nothing to clear */
  }
}
