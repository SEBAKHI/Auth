import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"
import { Button } from "@authsystem/ui/button"

import { useExternalProviders } from "@/components/use-external-providers"
import { Spinner } from "@authsystem/ui/spinner"

/** Minimal typings for the Sign in with Apple JS client. */
interface AppleSignInResponse {
  authorization: {
    id_token: string
    code: string
    state?: string
  }
  /** Only present on the very FIRST authorization for this Services ID. */
  user?: {
    email?: string
    name?: { firstName?: string; lastName?: string }
  }
}

interface AppleIdAuth {
  init: (config: {
    clientId: string
    scope: string
    redirectURI: string
    nonce?: string
    usePopup: boolean
  }) => void
  signIn: () => Promise<AppleSignInResponse>
}

declare global {
  interface Window {
    AppleID?: { auth: AppleIdAuth }
  }
}

/** Locales Apple's script actually ships; anything else falls back to en_US. */
const APPLE_LOCALES: Record<string, string> = {
  en: "en_US",
  ar: "ar_SA",
  tr: "tr_TR",
  fr: "fr_FR",
  zh: "zh_CN",
}

let appleScriptPromise: Promise<void> | null = null

/** Loads the Apple JS script once per page; resolves when AppleID is ready. */
function loadAppleScript(language: string): Promise<void> {
  appleScriptPromise ??= new Promise((resolve, reject) => {
    if (window.AppleID?.auth) {
      resolve()
      return
    }
    const locale = APPLE_LOCALES[language] ?? "en_US"
    const script = document.createElement("script")
    script.src = `https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/${locale}/appleid.auth.js`
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => {
      appleScriptPromise = null
      reject(new Error("Failed to load Sign in with Apple"))
    }
    document.head.appendChild(script)
  })
  return appleScriptPromise
}

/** The popup rejects with these when the user simply backs out — not errors. */
function isUserCancelled(error: unknown): boolean {
  const code = (error as { error?: string } | null)?.error
  return code === "popup_closed_by_user" || code === "user_cancelled_authorize"
}

interface LocationState {
  from?: { pathname?: string; search?: string }
}

/**
 * "Continue with Apple" button (popup ID-token flow). Renders nothing unless
 * the API lists an enabled "apple" provider AND a Services ID is configured.
 * The surrounding divider lives in ExternalProviders.
 *
 * Besides the ID token, the API receives the one-time authorization code — it
 * exchanges it server-side for the refresh token that later lets an account
 * deletion revoke the Apple grant — and, on first authorization only, the
 * user's name (Apple never repeats it).
 */
export function AppleSignIn() {
  const { i18n, t } = useTranslation()
  const { loginExternal } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const nonceRef = React.useRef<string>(crypto.randomUUID())
  const [pending, setPending] = React.useState(false)
  const { appleEnabled, appleServicesId } = useExternalProviders()

  const state = location.state as LocationState | null
  const from = state?.from?.pathname
    ? state.from.pathname + (state.from.search ?? "")
    : "/"

  const signIn = React.useCallback(async () => {
    setPending(true)
    let response: AppleSignInResponse | null = null
    try {
      await loadAppleScript(i18n.language)
      if (!window.AppleID) throw new Error("Failed to load Sign in with Apple")

      window.AppleID.auth.init({
        clientId: appleServicesId,
        scope: "name email",
        redirectURI: window.location.origin,
        nonce: nonceRef.current,
        usePopup: true,
      })
      response = await window.AppleID.auth.signIn()

      const result = await loginExternal(
        "apple",
        response.authorization.id_token,
        nonceRef.current,
        {
          authorizationCode: response.authorization.code,
          givenName: response.user?.name?.firstName,
          familyName: response.user?.name?.lastName,
        }
      )
      if (result.status === "twoFactorRequired") {
        navigate("/two-factor", {
          replace: true,
          state: { challengeToken: result.challengeToken, from },
        })
        return
      }
      toast.success(t("auth.welcomeBack"))
      if (result.requiresPasswordChange) {
        navigate("/force-password-change", { replace: true })
      } else {
        navigate(from, { replace: true })
      }
    } catch (error) {
      if (isUserCancelled(error)) return
      // Pending deletion (the ID token itself was valid): carry the still-
      // fresh credential to the recovery screen so restoring is one click.
      if (
        response &&
        getErrorCodes(error).includes("User.AccountPendingDeletion")
      ) {
        navigate("/account-recovery", {
          state: {
            message: getErrorMessage(error),
            external: {
              provider: "apple",
              idToken: response.authorization.id_token,
              nonce: nonceRef.current,
            },
          },
        })
        return
      }
      toast.error(getErrorMessage(error))
    } finally {
      setPending(false)
    }
  }, [appleServicesId, from, i18n.language, loginExternal, navigate, t])

  if (!appleEnabled) return null

  return (
    <div className="flex justify-center">
      <Button
        type="button"
        variant="outline"
        className="w-80"
        disabled={pending}
        onClick={() => void signIn()}
      >
        {pending ? (
          <Spinner />
        ) : (
          <svg
            data-icon="inline-start"
            viewBox="0 0 24 24"
            fill="currentColor"
            aria-hidden="true"
          >
            <path d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 3.014-2.117 3.675-.546 9.103 1.519 12.09 1.013 1.454 2.208 3.09 3.792 3.031 1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857-.026-3.04 2.48-4.494 2.597-4.559-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052-2.662.805-3.532 1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701" />
          </svg>
        )}
        {t("auth.continueWithApple")}
      </Button>
    </div>
  )
}
