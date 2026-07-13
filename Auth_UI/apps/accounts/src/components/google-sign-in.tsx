import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { GOOGLE_CLIENT_ID } from "@astoom/api/env"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { Separator } from "@astoom/ui/separator"
import { useTheme } from "@astoom/ui/theme-provider"

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

  const state = location.state as LocationState | null
  const from = state?.from?.pathname
    ? state.from.pathname + (state.from.search ?? "")
    : "/"

  const providersQuery = useQuery({
    queryKey: ["external-providers"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/external-providers")),
    staleTime: 5 * 60 * 1000,
  })

  const googleEnabled =
    GOOGLE_CLIENT_ID.length > 0 &&
    (providersQuery.data ?? []).some(
      (p) => p.code.toLowerCase() === "google"
    )

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
          client_id: GOOGLE_CLIENT_ID,
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
  }, [googleEnabled, i18n.language, resolvedTheme, onCredential])

  if (!googleEnabled) return null

  return (
    <div className="mt-6 space-y-4">
      <div className="flex items-center gap-3">
        <Separator className="flex-1" />
        <span className="text-xs text-muted-foreground">
          {t("auth.orContinueWith")}
        </span>
        <Separator className="flex-1" />
      </div>
      <div ref={containerRef} className="flex justify-center" />
    </div>
  )
}
