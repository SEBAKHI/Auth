import { useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorStatus } from "@authsystem/api/errors"
import { parseApiInstant } from "@authsystem/ui/api-instant"

import { ReauthenticateDialog } from "../reauthenticate-dialog"
import { AddPasskeyDialog, type PasskeyRegistration } from "./add-passkey-dialog"
import { passkeyFailureMessage, withRetryAfter } from "./passkey-errors"
import { loadWebAuthn, type PublicKeyCredentialCreationOptionsJSON } from "./webauthn"

/**
 * Adding a passkey (S20 PR C, PK15), in one place for every page that offers it (OI-280 PR B,
 * T-B7): the security page's passkey card, the offer after a sign-in, and the set-up page of a
 * two-step requirement. The flow is the card's, moved here because the account package depends on
 * this one and never the other way round.
 *
 * - The first click asks for the options and loads the helper. The server's gate decides, there
 *   and at every later step, what the person must give: a demand for a fresh proof opens the
 *   window where the person stands (the passkey alone for an account whose factor is one, T-C7),
 *   and the addition goes on from there.
 * - Then a dialog (`AddPasskeyDialog`): a name, the emailed code when the gate asks for one, and
 *   the confirm, which calls `startRegistration` before awaiting anything (T-C2). It also sends the
 *   same answer again with the code when the time a sign-in by code counts for ran out between the
 *   options and the bind (T-B5).
 * - `Passkey.EmailRequired` (email switched off since the page read the account) is said in a
 *   plain sentence that names the way out, and the account is read again, so the page stops
 *   offering what fails (N9).
 */

/** The server's registration challenge lives five minutes. */
const OPTIONS_MAX_LIFE_MS = 5 * 60_000
/**
 * Options are issued fresh for each registration, so one that seems to have less
 * than this left says this device's clock runs ahead of the server's: its word
 * would close the dialog at every press, and the person would never know why.
 */
const OPTIONS_CREDIBLE_LIFE_MS = 60_000

/**
 * Until when the options may be used, on `performance.now()`: the life left at
 * receipt, measured from then on by a clock nobody can set. Null when the
 * device's clock disagrees with the server's: nothing is decided here then, and
 * the server's own answer (`Passkey.VerificationFailed`) starts the setup again.
 */
function optionsUsableUntil(expiresAt: string): number | null {
  const life = (parseApiInstant(expiresAt)?.getTime() ?? Number.NaN) - Date.now()
  if (!(life > OPTIONS_CREDIBLE_LIFE_MS)) return null
  return performance.now() + Math.min(life, OPTIONS_MAX_LIFE_MS)
}

export interface PasskeyAdditionOptions {
  /**
   * The account's only qualifying factor is a passkey, and passkeys run: adding needs a two-factor
   * session, which comes from the passkey alone (PK7, T-C7).
   */
  passkeyOnly: boolean
  /** What the page says about the addition, or null to clear it: one place on each page. */
  onMessage: (message: string | null) => void
  /** The passkey was added, under the name the server kept. */
  onAdded: (name: string) => void
  /** The dialog closed with nothing added, by Cancel or Escape. */
  onClosed?: () => void
  /**
   * The window's way out for a page that must not sign out: an application's pending request
   * would be lost (S23). Absent, the way out signs out, as everywhere else.
   */
  onSignInAgain?: () => void
  /**
   * The options answered 404: passkeys were switched off since the page decided to offer one.
   * The page's sentence for it is said either way; a page that can drop its passkey choice does.
   */
  onUnavailable?: () => void
}

export interface PasskeyAddition {
  /** The first click: the options, or the window when the gate asks for a fresh proof. */
  begin: () => Promise<void>
  /** The options are on their way. */
  starting: boolean
  /** The dialog and the window, mounted while needed; render once, anywhere on the page. */
  dialogs: React.ReactNode
}

/** The addition of a passkey, for a page to start from its own button. */
export function usePasskeyAddition({
  passkeyOnly,
  onMessage,
  onAdded,
  onClosed,
  onSignInAgain,
  onUnavailable,
}: PasskeyAdditionOptions): PasskeyAddition {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [registration, setRegistration] = React.useState<PasskeyRegistration | null>(null)
  // One counter, so a dialog reopened for a new registration mounts afresh.
  const [registrationSeq, setRegistrationSeq] = React.useState(0)
  const [starting, setStarting] = React.useState(false)
  const startingRef = React.useRef(false)
  const [reauthenticating, setReauthenticating] = React.useState(false)

  // Email went off since the page read the account (N9): said plainly, with the way out (the app
  // first, then the passkey: the card's own sentence for N9), and the account read again, so the
  // page stops offering an addition that cannot go through.
  const emailRequired = React.useCallback(() => {
    onMessage(t("profile.passkeys.needsAppFirst"))
    void queryClient.invalidateQueries({ queryKey: ["me"], exact: true })
  }, [onMessage, queryClient, t])

  const begin = React.useCallback(async () => {
    if (startingRef.current) return
    startingRef.current = true
    setStarting(true)
    onMessage(null)
    try {
      const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/options")
      if (error || !data) throw withRetryAfter(error ?? new Error("Passkey options failed"), response)
      // In hand before the confirm, which must call it without awaiting (T-C2).
      const helper = await loadWebAuthn()
      setRegistration({
        options: data.options as PublicKeyCredentialCreationOptionsJSON,
        usableUntil: optionsUsableUntil(data.expiresAt),
        emailCodeRequired: data.emailCodeRequired,
        helper,
      })
      setRegistrationSeq((seq) => seq + 1)
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("Auth.ReauthenticationRequired")) {
        setReauthenticating(true)
      } else if (codes.includes("Passkey.EmailRequired")) {
        emailRequired()
      } else {
        onMessage(passkeyFailureMessage(error))
        if (getErrorStatus(error) === 404) onUnavailable?.()
      }
    } finally {
      startingRef.current = false
      setStarting(false)
    }
  }, [emailRequired, onMessage, onUnavailable])

  const dialogs = (
    <>
      {/* Mounted only while a registration is open, so closing it resets it. */}
      {registration ? (
        <AddPasskeyDialog
          key={`add-passkey-${registrationSeq}`}
          registration={registration}
          onClose={() => {
            setRegistration(null)
            onClosed?.()
          }}
          onAdded={(name) => {
            setRegistration(null)
            onAdded(name)
          }}
          onStartAgain={() => {
            setRegistration(null)
            void begin()
          }}
          onReauthenticate={() => {
            setRegistration(null)
            setReauthenticating(true)
          }}
          onEmailRequired={() => {
            setRegistration(null)
            emailRequired()
          }}
        />
      ) : null}

      {reauthenticating ? (
        <ReauthenticateDialog
          open
          onOpenChange={(open) => !open && setReauthenticating(false)}
          description={t("profile.passkeys.needsRecentSignIn")}
          passkey={passkeyOnly}
          onProved={() => void begin()}
          onSignInAgain={onSignInAgain}
        />
      ) : null}
    </>
  )

  return { begin, starting, dialogs }
}
