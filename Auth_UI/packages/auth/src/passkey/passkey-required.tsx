/* eslint-disable react-refresh/only-export-components */
import * as React from "react"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"

/**
 * The one reader of «this answer asks for the passkey» (S20 PR C, IN 5).
 *
 * An account that administers the platform and whose only usable second factor
 * is a passkey signs in with that passkey alone (owner decision D-S20-4 (ii-s)):
 * the password, a provider, the emailed code, the recovery's three entries, the
 * sign-in after a verified address and the second step all answer it with
 * `Auth.PasskeyRequired`. Every page that sends one of those shows the API's own
 * sentence where the person is looking, never as a toast that goes, and offers
 * the passkey: the sign-in page its passkey button, the recovery page its
 * passkey choice, and the two backstops a way back to the sign-in page.
 *
 * `passkey-required.test.tsx` fails for a sign-in or recovery caller that does
 * not ask this, the way `deletionCancelledOutcome` is asked by the recovery
 * pages.
 */
export function passkeyRequiredNotice(error: unknown): string | null {
  return getErrorCodes(error).includes("Auth.PasskeyRequired") ? getErrorMessage(error) : null
}

/**
 * The sign-in page's answer to a door that asked for the passkey, for the doors
 * it does not render itself: the provider buttons under its form. Absent, the
 * door shows the refusal as it shows any other.
 */
const PasskeyRequiredContext = React.createContext<((notice: string) => void) | null>(null)

export function PasskeyRequiredProvider({
  onPasskeyRequired,
  children,
}: {
  onPasskeyRequired: (notice: string) => void
  children: React.ReactNode
}) {
  return (
    <PasskeyRequiredContext.Provider value={onPasskeyRequired}>
      {children}
    </PasskeyRequiredContext.Provider>
  )
}

/**
 * Hands a refusal to the page when it asks for the passkey: true when the page
 * took it, false for any other failure, which the caller handles as before.
 */
export function usePasskeyRequiredReporter(): (error: unknown) => boolean {
  const report = React.useContext(PasskeyRequiredContext)
  return React.useCallback(
    (error: unknown) => {
      const notice = passkeyRequiredNotice(error)
      if (!notice || !report) return false
      report(notice)
      return true
    },
    [report]
  )
}
