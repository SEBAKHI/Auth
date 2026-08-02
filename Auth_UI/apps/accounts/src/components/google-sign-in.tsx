import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"
import { useTheme } from "@authsystem/ui/theme-provider"

import { useExternalProviders } from "@/components/use-external-providers"

/** Minimal typings for the Google Identity Services (GSI) client. */
interface GsiIdConfiguration {
  client_id: string
  callback: (response: { credential: string }) => void
  nonce?: string
}

interface GsiButtonConfiguration {
  type: "standard"
  theme: "outline" | "filled_black"
  size: "large"
  text: "continue_with"
  width?: number
  locale?: string
}

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: GsiIdConfiguration) => void
          renderButton: (
            parent: HTMLElement,
            options: GsiButtonConfiguration
          ) => void
        }
      }
    }
  }
}

const GSI_SRC = "https://accounts.google.com/gsi/client"

let gsiScriptPromise: Promise<void> | null = null

/** Loads the GSI script once per page; resolves when window.google is ready. */
function loadGsiScript(): Promise<void> {
  gsiScriptPromise ??= new Promise((resolve, reject) => {
    if (window.google?.accounts?.id) {
      resolve()
      return
    }
    const script = document.createElement("script")
    script.src = GSI_SRC
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => {
      gsiScriptPromise = null
      reject(new Error("Failed to load Google Identity Services"))
    }
    document.head.appendChild(script)
  })
  return gsiScriptPromise
}

interface LocationState {
  from?: { pathname?: string; search?: string }
}

/**
 * "Continue with Google" button (GSI ID-token flow). Renders nothing unless
 * the API lists an enabled "google" provider AND a client id is configured.
 * The surrounding divider lives in ExternalProviders.
 *
 * A fresh nonce is generated per mount and sent BOTH to Google (echoed inside
 * the signed ID token) and to the API, which rejects the login on mismatch —
 * the API only validates the nonce when one is provided, so always sending it
 * is what turns replay protection on.
 */
export function GoogleSignIn() {
  const { i18n, t } = useTranslation()
  const { loginExternal } = useAuth()
  const { resolvedTheme } = useTheme()
  const navigate = useNavigate()
  const location = useLocation()
  const containerRef = React.useRef<HTMLDivElement>(null)
  const nonceRef = React.useRef<string>(crypto.randomUUID())
  const { googleEnabled, googleClientId } = useExternalProviders()

  const state = location.state as LocationState | null
  const from = state?.from?.pathname
    ? state.from.pathname + (state.from.search ?? "")
    : "/"

  const onCredential = React.useCallback(
    async (credential: string) => {
      try {
        const result = await loginExternal(
          "google",
          credential,
          nonceRef.current
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
        // Pending deletion (the ID token itself was valid): carry the still-
        // fresh credential to the recovery screen so restoring is one click.
        if (getErrorCodes(error).includes("User.AccountPendingDeletion")) {
          navigate("/account-recovery", {
            state: {
              message: getErrorMessage(error),
              external: {
                provider: "google",
                idToken: credential,
                nonce: nonceRef.current,
              },
            },
          })
          return
        }
        toast.error(getErrorMessage(error))
      }
    },
    [loginExternal, navigate, from, t]
  )

  React.useEffect(() => {
    if (!googleEnabled) return
    let cancelled = false

    void loadGsiScript()
      .then(() => {
        if (cancelled || !containerRef.current || !window.google) return
        window.google.accounts.id.initialize({
          client_id: googleClientId,
          nonce: nonceRef.current,
          callback: (response) => void onCredential(response.credential),
        })
        containerRef.current.replaceChildren()
        window.google.accounts.id.renderButton(containerRef.current, {
          type: "standard",
          theme: resolvedTheme === "dark" ? "filled_black" : "outline",
          size: "large",
          text: "continue_with",
          width: 320,
          locale: i18n.language,
        })
      })
      .catch(() => {
        /* Script blocked/offline: the button simply doesn't render. */
      })

    return () => {
      cancelled = true
    }
  }, [googleEnabled, googleClientId, i18n.language, resolvedTheme, onCredential])

  if (!googleEnabled) return null

  return <div ref={containerRef} className="flex justify-center" />
}
