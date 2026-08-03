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
  /**
   * "medium" rather than "large" is a deliberate trade.
   *
   * Google exposes no switch to turn off the PERSONALIZED button — the variant
   * that greets a returning visitor by name and email, and that keeps doing so
   * after they sign out of THIS app, because it follows the browser's Google
   * session and not ours. What its UX guide does document is that the
   * personalized button is not displayed when size is "medium" or "small".
   *
   * Verified against a live Google session: at "large" the button reads
   * "Continue as <name>" over the address; at "medium" it reads plain
   * "Continue with Google". The cost is 8px of height (40 -> 32) with the width
   * unchanged, which also brings it closer to the Apple button's h-9.
   *
   * This is a documented side effect, not an API, so Google could drop it. To
   * go back to the personalized button, put "large" here and at the call site;
   * nothing else depends on it.
   */
  size: "large" | "medium" | "small"
  text: "continue_with"
  /**
   * Google's SDK always renders the CURRENT branding, so the button only looks
   * dated when we ask for a dated variant. "pill" is the rounded shape Google's
   * own sign-in surfaces use; the default is the older square-cornered
   * "rectangular".
   *
   * logo_alignment stays "left": "center" packs logo and label together and, at
   * a fixed width, a longer translation ("Continuer avec Google") slides under
   * the logo and loses its first character.
   */
  shape?: "rectangular" | "pill" | "circle" | "square"
  logo_alignment?: "left" | "center"
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

/**
 * The GSI library resolves the button language from the `hl` parameter on its
 * own script URL. The `locale` field passed to renderButton alone is not
 * enough — without `hl` Google falls back to "the browser's default locale or
 * the Google session user's preference", which is why the button used to read
 * in whatever language the visitor's Google account happened to use while the
 * rest of the page was in ours.
 *
 * That parameter is fixed once the script is fetched, so switching language has
 * to fetch a new one. The promise is therefore keyed BY LANGUAGE rather than
 * cached once per page: each language loads at most one script, and the button
 * follows the site instead of the Google session.
 */
const gsiScriptPromises = new Map<string, Promise<void>>()

/** Maps an i18n tag ("ar", "zh-CN") to the ISO-639 code GSI expects. */
function gsiLocale(language: string): string {
  return language.split("-")[0].toLowerCase()
}

function loadGsiScript(language: string): Promise<void> {
  const locale = gsiLocale(language)
  const existing = gsiScriptPromises.get(locale)
  if (existing) return existing

  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement("script")
    script.src = `${GSI_SRC}?hl=${encodeURIComponent(locale)}`
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => {
      gsiScriptPromises.delete(locale)
      reject(new Error("Failed to load Google Identity Services"))
    }
    document.head.appendChild(script)
  })

  gsiScriptPromises.set(locale, promise)
  return promise
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

    void loadGsiScript(i18n.language)
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
          // Suppresses the personalized button — see GsiButtonConfiguration.
          size: "medium",
          text: "continue_with",
          shape: "pill",
          logo_alignment: "left",
          width: 320,
          // Sent as well as `hl`: the script parameter selects the library's
          // language bundle, this selects the label within it.
          locale: gsiLocale(i18n.language),
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

  /*
   * `scheme-light` (color-scheme: light) is load-bearing, not cosmetic.
   *
   * renderButton has two rendering paths: plain DOM in our page, or an
   * accounts.google.com/gsi/button iframe. It takes the iframe path once the
   * visitor has an active Google session, which is why the defect only
   * surfaces after signing in with Google at least once. Verified against a
   * live session that it takes that path for the generic label too, so this
   * stays load-bearing even with the personalized button suppressed above.
   *
   * The iframe's document declares a LIGHT color scheme. Our dark theme sets
   * `color-scheme: dark` on the root (preset.css) and the iframe element
   * inherits it, so the two disagree — and css-color-adjust-1 then requires the
   * UA to "use an opaque canvas of the Canvas color appropriate to the embedded
   * document's root element's element color scheme instead of a transparent
   * canvas". That opaque canvas is the white rectangle painted around Google's
   * filled_black pill, and no stylesheet of ours can reach it: the canvas
   * belongs to Google's document.
   *
   * Declaring light on the container makes frame and content agree, so the
   * canvas stays transparent and the pill sits on our own background. It is a
   * no-op in light mode, and it does NOT lighten the button: the button's
   * colours come from the `theme` argument above, which stays filled_black in
   * dark mode per Google's branding guidelines.
   */
  return <div ref={containerRef} className="flex justify-center scheme-light" />
}
