import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { GoogleIcon } from "@authsystem/ui/brand-icons"
import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"

import { useLoginCompletion } from "../login-completion"
import { usePasskeyRequiredReporter } from "../passkey/passkey-required"
import { useStaySignedIn } from "../stay-signed-in"
import {
  buildAuthorizeUrl,
  GOOGLE_CALLBACK_PATH,
  GOOGLE_CHANNEL,
  googleLocale,
  isGoogleCallbackMessage,
  pkceChallenge,
  randomBase64Url,
  type GoogleCallbackMessage,
} from "./google-code-flow"
import { navigateToRecovery } from "./recovery-navigation"
import { requestExternalNonce } from "./request-nonce"
import {
  externalProvidersQuery,
  resolveExternalProviders,
  useExternalProviders,
} from "./use-external-providers"

/**
 * A fresh window name per attempt. A fixed, public name would let another page in the same
 * window group (one that opened this sign-in page, say) find the popup by that name and
 * navigate it to a page of its own while the person expects Google's (review of #162).
 */
function popupName(): string {
  return `google-sign-in-${randomBase64Url().slice(0, 16)}`
}
const POPUP_WIDTH = 500
const POPUP_HEIGHT = 640
/** How often the popup handle is asked whether the person closed it. */
const CLOSED_POLL_MS = 500

/** A small window centred on this one. */
function popupFeatures(): string {
  const left = Math.max(0, Math.round(window.screenX + (window.outerWidth - POPUP_WIDTH) / 2))
  const top = Math.max(0, Math.round(window.screenY + (window.outerHeight - POPUP_HEIGHT) / 2))
  return `popup,width=${POPUP_WIDTH},height=${POPUP_HEIGHT},left=${left},top=${top}`
}

/** One click's sign-in: what Google must echo back, and what the exchange needs. */
interface Attempt {
  popup: Window
  state: string
  verifier: string
  /** The server's nonce, sent to Google; known once the attempt reached Google. */
  nonce?: string
}

interface GoogleSignInProps {
  /**
   * Where to send an account that is pending deletion — a route in this app, or
   * an absolute URL when the recovery screen lives on another origin (the
   * console points at the accounts app). Without it the branch degrades to the
   * error toast, which is a dead end: the toast states that the account is
   * scheduled for deletion and offers nothing to act on.
   */
  recoveryPath?: string
  /**
   * Capture mode. When supplied, the button does NOT sign in: it hands the
   * provider credential to the caller and does nothing else.
   *
   * This exists for the recovery screen, which needs the same credential for a
   * different call (`/deletion/recover-external` rather than `/external-login`).
   * The alternative considered was to let it sign in normally, fail with the
   * pending-deletion error and re-enter the screen carrying the credential —
   * cheaper to write, but it makes a deliberately failing request a control
   * flow, and it breaks the day failed external logins get rate-limited.
   */
  onCredential?: (credential: {
    provider: string
    idToken: string
    nonce?: string
  }) => void
}

/**
 * "Continue with Google" (S02 PR D): our own button, and Google's authorization-code method.
 *
 * The button is the preset's outline button with Google's standard "G" (owner decision
 * D-S02-6 (a)). It always reads "Continue with Google", never a person's name or photo, and it
 * shows with the page: on the providers hint this browser kept from its last visit, before the
 * live answer is in. No Google script, style or frame is ever loaded into this page.
 *
 * A click opens a popup at once (a window opened after an `await` is blocked), then sends it to
 * Google's authorization page with a fresh `state`, a PKCE challenge and the server-issued
 * nonce. Google returns the person to /google-callback.html with a one-time code; that page
 * hands it back here (google-callback-page.ts), the API exchanges it for the ID token, and the
 * token goes through today's sign-in unchanged: `external-login` checks its signature, audience
 * and nonce, and every exit goes through useLoginCompletion.
 *
 * The nonce is fetched from the API before the click when Google is offered (the API keeps its
 * matching half in an HttpOnly cookie), and sealed by Google into the token: the pair shows the
 * token was minted for THIS browser, which is what makes a stolen token useless here.
 */
export function GoogleSignIn({
  recoveryPath,
  onCredential,
}: GoogleSignInProps = {}) {
  const { i18n, t } = useTranslation()
  const { loginExternal } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { googleEnabled, googleExpected } = useExternalProviders()
  const { complete, challenge } = useLoginCompletion()
  const readStaySignedIn = useStaySignedIn()
  // The account signs in with its passkey alone (S20): the sign-in page says so.
  const reportPasskeyRequired = usePasskeyRequiredReporter()

  // The server's nonce, asked for as soon as Google is offered, so a click rarely waits for it.
  // Taken by one attempt; the next click asks again (a new nonce replaces the cookie's half).
  const earlyNonce = React.useRef<Promise<string> | null>(null)
  // The attempt Google may answer. A second click replaces it, and the first is abandoned.
  const attemptRef = React.useRef<Attempt | null>(null)
  // "waiting": the popup is open at Google; "exchanging": the code is with the API.
  const [phase, setPhase] = React.useState<"idle" | "waiting" | "exchanging">("idle")
  const [blocked, setBlocked] = React.useState(false)

  React.useEffect(() => {
    if (!googleExpected) return
    earlyNonce.current ??= requestExternalNonce()
  }, [googleExpected])

  const handleCredential = React.useCallback(
    async (credential: string, nonce: string | undefined) => {
      // Capture mode: the caller wants the credential, not a session.
      if (onCredential) {
        onCredential({ provider: "google", idToken: credential, nonce })
        return
      }

      try {
        const result = await loginExternal("google", credential, nonce, {
          staySignedIn: readStaySignedIn(),
        })
        if (result.status === "twoFactorRequired") {
          challenge(result.challengeToken, result.methods)
          return
        }
        toast.success(t("auth.welcomeBack"))
        complete(result, { proof: "external" })
      } catch (error) {
        // Pending deletion (the ID token itself was valid): carry the still-
        // fresh credential to the recovery screen so restoring is one click.
        // Only where the host app has one — see recoveryPath.
        if (
          recoveryPath &&
          getErrorCodes(error).includes("User.AccountPendingDeletion")
        ) {
          navigateToRecovery(navigate, recoveryPath, {
            message: getErrorMessage(error),
            external: { provider: "google", idToken: credential, nonce },
          })
          return
        }
        if (reportPasskeyRequired(error)) return
        toast.error(getErrorMessage(error))
      }
    },
    [
      loginExternal,
      navigate,
      complete,
      challenge,
      t,
      recoveryPath,
      onCredential,
      readStaySignedIn,
      reportPasskeyRequired,
    ]
  )

  // The answer of the callback page: on the same-origin channel, and by postMessage from a
  // popup that still has its opener. Taken once, and only for the attempt it names.
  const receive = React.useCallback(
    async (message: GoogleCallbackMessage) => {
      const attempt = attemptRef.current
      if (!attempt || message.state !== attempt.state) return
      attemptRef.current = null
      try {
        attempt.popup.close()
      } catch {
        // Already closed, or no longer ours to close.
      }

      if (message.type === "google-error") {
        setPhase("idle")
        // The person backed out at Google: nothing to report, as with Apple's cancel.
        if (message.error !== "access_denied") {
          toast.error(getErrorMessage(new Error(message.error)))
        }
        return
      }

      setPhase("exchanging")
      try {
        const { idToken } = await unwrap(
          api.POST("/api/v1/Auth/external/google/code", {
            body: { code: message.code, codeVerifier: attempt.verifier },
          })
        )
        await handleCredential(idToken, attempt.nonce)
      } catch (error) {
        toast.error(getErrorMessage(error))
      } finally {
        setPhase("idle")
      }
    },
    [handleCredential]
  )

  React.useEffect(() => {
    const onMessage = (data: unknown) => {
      if (isGoogleCallbackMessage(data)) void receive(data)
    }
    let channel: BroadcastChannel | null = null
    try {
      channel = new BroadcastChannel(GOOGLE_CHANNEL)
      channel.onmessage = (event: MessageEvent) => onMessage(event.data)
    } catch {
      // No BroadcastChannel: the opener's postMessage below is the only way back.
    }
    const onWindowMessage = (event: MessageEvent) => {
      if (event.origin !== window.location.origin) return
      onMessage(event.data)
    }
    window.addEventListener("message", onWindowMessage)
    return () => {
      channel?.close()
      window.removeEventListener("message", onWindowMessage)
    }
  }, [receive])

  // Ends the wait when the person closes the popup without answering. When Google cut the
  // popup's link to this page, the handle reports closed while the window is still open: the
  // wait ends then too, and the attempt keeps listening, so an answer that still arrives is
  // still taken.
  React.useEffect(() => {
    if (phase !== "waiting") return
    const timer = window.setInterval(() => {
      const attempt = attemptRef.current
      if (!attempt || attempt.popup.closed) setPhase((current) => (current === "waiting" ? "idle" : current))
    }, CLOSED_POLL_MS)
    return () => window.clearInterval(timer)
  }, [phase])

  const start = () => {
    setBlocked(false)
    // A previous attempt's window, if it is still ours to close: this one replaces it.
    try {
      attemptRef.current?.popup.close()
    } catch {
      // Already closed, or no longer ours.
    }
    // Synchronously, inside the click: a window opened after an await is a blocked popup.
    const popup = window.open("about:blank", popupName(), popupFeatures())
    if (!popup) {
      setBlocked(true)
      return
    }

    const attempt: Attempt = { popup, state: randomBase64Url(), verifier: randomBase64Url() }
    attemptRef.current = attempt
    setPhase("waiting")

    const nonce = earlyNonce.current ?? requestExternalNonce()
    earlyNonce.current = null

    void (async () => {
      try {
        const [serverNonce, providers, codeChallenge] = await Promise.all([
          nonce,
          queryClient.fetchQuery(externalProvidersQuery()),
          pkceChallenge(attempt.verifier),
        ])
        if (attemptRef.current !== attempt) return
        if (popup.closed) {
          // The person closed the blank window before it reached Google: nothing to report.
          attemptRef.current = null
          setPhase("idle")
          return
        }
        const { googleEnabled: enabled, googleClientId } = resolveExternalProviders(providers)
        if (!enabled) {
          // Google was switched off since this page drew the button: the cache now says so,
          // and the button and its divider leave with it.
          attemptRef.current = null
          popup.close()
          setPhase("idle")
          return
        }
        attempt.nonce = serverNonce
        popup.location.replace(
          buildAuthorizeUrl({
            clientId: googleClientId,
            redirectUri: window.location.origin + GOOGLE_CALLBACK_PATH,
            state: attempt.state,
            nonce: serverNonce,
            codeChallenge,
            locale: googleLocale(i18n.language),
          })
        )
      } catch (error) {
        if (attemptRef.current !== attempt) return
        attemptRef.current = null
        popup.close()
        setPhase("idle")
        toast.error(getErrorMessage(error))
      }
    })()
  }

  // While Google's window is open, a click brings it forward and opens no other: clicking this
  // page sends the popup behind it, and a disabled button would leave nothing on the page to
  // bring it back with (REVIEW 162 F4). A handle that reports closed (the person closed it, or
  // Google cut its link to this page) starts a new attempt.
  const onClick = () => {
    const attempt = attemptRef.current
    if (phase === "waiting" && attempt && !attempt.popup.closed) {
      try {
        attempt.popup.focus()
        return
      } catch {
        // No longer ours to focus: a new attempt replaces it.
      }
    }
    start()
  }

  if (!googleEnabled && !googleExpected) return null
  // Only the exchange is work this page waits on; while Google's window is open the page waits
  // on the person, and the button stays usable.
  const exchanging = phase === "exchanging"

  return (
    <div className="flex flex-col items-center gap-2">
      <Button
        type="button"
        variant="outline"
        className="w-80"
        disabled={exchanging}
        aria-busy={exchanging}
        onClick={onClick}
      >
        {exchanging ? <Spinner /> : <GoogleIcon data-icon="inline-start" />}
        {t("auth.continueWithGoogle")}
      </Button>
      {blocked ? (
        <p role="alert" className="w-80 text-sm text-destructive">
          {t("auth.googlePopupBlocked")}
        </p>
      ) : null}
    </div>
  )
}
