import type { NavigateFunction } from "react-router-dom"

/**
 * The still-fresh provider credential, carried to the recovery screen so that
 * restoring an account is one click rather than a second sign-in.
 */
export interface ExternalCredential {
  provider: string
  idToken: string
  nonce?: string
}

/**
 * Sends a pending-deletion account to wherever this app's recovery screen
 * lives, which is not always this app.
 *
 * The console has no recovery screen of its own and should not grow one: it is
 * an administration surface, account lifecycle belongs to the accounts app, and
 * its CSP was widened for a provider script only recently. So it points at the
 * accounts origin instead, and an absolute target means a full page load rather
 * than a client-side route.
 *
 * Router state cannot survive that navigation, so the credential is dropped on
 * the cross-origin path. That is affordable now and was not before: the
 * recovery screen obtains its own credential from the provider buttons it now
 * renders, so arriving empty-handed costs one extra click instead of being a
 * dead end.
 *
 * `recoveryPath` is a compile-time constant at every call site and must stay
 * one. Deriving it from a query string or from router state would turn this
 * into an open redirect — external-providers.test.ts pins that.
 */
export function navigateToRecovery(
  navigate: NavigateFunction,
  recoveryPath: string,
  state: { message: string; external: ExternalCredential }
): void {
  if (isAbsoluteUrl(recoveryPath)) {
    window.location.assign(recoveryPath)
    return
  }

  navigate(recoveryPath, { state })
}

/** True for a target on another origin, e.g. `https://accounts.example.com/x`. */
export function isAbsoluteUrl(target: string): boolean {
  return /^https?:\/\//i.test(target)
}
