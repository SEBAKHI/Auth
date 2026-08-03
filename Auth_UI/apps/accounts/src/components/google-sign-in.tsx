import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"
import { Button } from "@authsystem/ui/button"

import { useExternalProviders } from "@/components/use-external-providers"

/**
 * Google's official "G", copied from the mark their own SDK renders. Their
 * branding guidelines require this exact artwork, so it does not follow
 * `currentColor` the way our other icons do.
 */
function GoogleLogo(props: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 48 48" aria-hidden="true" {...props}>
      <path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </svg>
  )
}

/** Minimal typings for the Google Identity Services (GSI) client. */
interface GsiIdConfiguration {
  client_id: string
  callback: (response: { credential: string }) => void
  nonce?: string
}

/**
 * Everything here sizes Google's button rather than styling it: the visitor
 * never sees it (see the render below), so only its HIT AREA matters. "large"
 * at width 320 gives a 320x40 target over our own 320x36 button, covering it
 * with 2px to spare on each edge.
 */
interface GsiButtonConfiguration {
  type: "standard"
  theme: "outline" | "filled_black"
  size: "large"
  text: "continue_with"
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
  const navigate = useNavigate()
  const location = useLocation()
  const containerRef = React.useRef<HTMLDivElement>(null)
  const [ready, setReady] = React.useState(false)
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
          // Invisible, so this is arbitrary — see GsiButtonConfiguration.
          theme: "outline",
          size: "large",
          text: "continue_with",
          shape: "pill",
          logo_alignment: "left",
          width: 320,
          // Sent as well as `hl`: the script parameter selects the library's
          // language bundle, this selects the label within it. Google still
          // reads the label out to assistive tech, so it has to stay in ours.
          locale: gsiLocale(i18n.language),
        })
        setReady(true)
      })
      .catch(() => {
        /* Script blocked/offline: nothing renders, as before. */
      })

    return () => {
      cancelled = true
    }
  }, [googleEnabled, googleClientId, i18n.language, onCredential])

  if (!googleEnabled) return null

  /*
   * Two stacked buttons: ours is seen, Google's is clicked.
   *
   * renderButton is the only way GSI hands us an ID token — there is no API to
   * start the flow from a button of our own — but what it draws is not ours to
   * control. It is locked to 20/32/40px (our Button is 36), it is drawn inside
   * an accounts.google.com iframe as soon as the visitor has a Google session,
   * and at 40px that iframe shows the PERSONALIZED button, greeting a returning
   * visitor by name and email long after they signed out of this app — the
   * button follows the browser's Google session, not ours.
   *
   * So Google's button is kept at full size, made transparent, and laid over a
   * Button of ours. Every click still lands on Google's real button inside its
   * own frame, which is what keeps the ID-token flow and the `external-login`
   * endpoint untouched; only the pixels are ours. This also retires the
   * `color-scheme` workaround the visible iframe needed in dark mode, since
   * nothing of Google's is painted any more.
   *
   * Consequences that have to be honoured for this to stay correct:
   *   - Ours is decorative: aria-hidden and untabbable, so assistive tech and
   *     the keyboard reach Google's real button and its label instead. The
   *     focus ring is mirrored through focus-within, otherwise tabbing would
   *     land on an invisible control with nothing to show for it.
   *   - It renders only once renderButton has succeeded. Drawn eagerly, a
   *     blocked GSI script would leave a button that looks alive and does
   *     nothing.
   *   - The overlay must stay at least as large as ours, or clicks near the
   *     edges would fall through to the page.
   */
  return (
    <div className="group/google relative flex justify-center">
      {ready ? (
        <Button
          type="button"
          variant="outline"
          aria-hidden="true"
          tabIndex={-1}
          className="pointer-events-none w-80 group-focus-within/google:border-ring group-focus-within/google:ring-3 group-focus-within/google:ring-ring/30"
        >
          <GoogleLogo data-icon="inline-start" />
          {t("auth.continueWithGoogle")}
        </Button>
      ) : null}
      <div
        ref={containerRef}
        className="absolute inset-0 flex items-center justify-center opacity-0"
      />
    </div>
  )
}
