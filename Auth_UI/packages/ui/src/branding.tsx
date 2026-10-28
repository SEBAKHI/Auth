import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { useTheme } from "@authsystem/ui/theme-provider"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { API_BASE_URL } from "@authsystem/api/env"
import type { Schemas } from "@authsystem/api/types"
import {
  applyThemeCss,
  readCachedTheme,
  writeCachedTheme,
} from "@authsystem/ui/theme/apply-theme"
import {
  DEFAULT_THEME_CONFIG,
  THEME_ENGINE_VERSION,
  themeConfigKey,
  toThemeConfig,
  type ThemeConfig,
} from "@authsystem/ui/theme/theme-config"

const DEFAULT_THEME_KEY = themeConfigKey(DEFAULT_THEME_CONFIG)

export const BRANDING_QUERY_KEY = ["platform-branding"] as const

const DEFAULT_FAVICON = "/vite.svg"
const BRANDING_CACHE_KEY = "auth.ui.branding"

type BrandingPayload = Schemas["PlatformBrandingDto"]

interface BrandingValue {
  /** Platform display name (admin-configured, falls back to the app default). */
  name: string
  /**
   * Absolute logo URL for the active theme (falls back to the other theme's
   * logo), or null when no logo has been uploaded.
   */
  logoUrl: string | null
  /**
   * True only while the branding is genuinely unknown — no cached copy and no
   * response yet.
   *
   * Without it, "not fetched yet" was indistinguishable from "no logo
   * uploaded", so every cold load painted the default shield and the default
   * product name and then replaced both. Consumers must render a reservation
   * rather than a guess while this is set.
   */
  isPending: boolean
  /**
   * Whether the sign-in page offers "Stay signed in" (OI-165): false when the
   * administrator decides for everyone. The server applies that decision to
   * every sign-in whatever the page sends, so this only decides what is shown:
   * true when the payload lacks the field (an older API), or when the request
   * failed with no cached copy, as before the setting existed (a cached answer
   * followed by a failed refetch keeps the cached answer). Null while nothing
   * is known yet: the page then holds the box's place without showing it, so
   * the box neither flashes in and out where it is hidden nor pushes the
   * button down where it is shown. A copy cached from a previous visit may
   * answer first; the refetch corrects it. It is the deployment's setting,
   * never the person's tick.
   */
  staySignedInOffered: boolean | null
  /**
   * The sign-in page's options (OI-215): whether a code sent by email is
   * offered, which method comes first, and what a sign-up asks for. A hint for
   * what is shown, cached with the rest of the branding: the server decides every
   * request on its own settings. Null while nothing is known yet. A payload
   * without the fields (an older API, or a copy cached before them) offers no
   * code, which is the page every visitor always saw.
   */
  signInOptions: SignInOptions | null
}

export type SignInMethod = "password" | "code"

export interface SignInOptions {
  /** Whether the page offers a code sent by email (email sending is on). */
  codeSignInOffered: boolean
  /** The method the page puts first; the other stays a visible link. */
  primaryMethod: SignInMethod
  /** Whether a visitor may create an account. */
  selfRegistrationOpen: boolean
  /** Whether a new account must set a password. */
  passwordAtSignUp: boolean
  /** Whether a new account must give a first and last name. */
  nameAtSignUp: boolean
}

/** What the page offers when the payload says nothing of the code sign-in. */
const PASSWORD_ONLY: SignInOptions = {
  codeSignInOffered: false,
  primaryMethod: "password",
  selfRegistrationOpen: true,
  passwordAtSignUp: true,
  nameAtSignUp: true,
}

export function toSignInOptions(payload: {
  codeSignInOffered?: boolean
  primaryMethod?: string
  selfRegistrationOpen?: boolean
  passwordAtSignUp?: boolean
  nameAtSignUp?: boolean
}): SignInOptions {
  if (payload.codeSignInOffered === undefined) return PASSWORD_ONLY
  const codeSignInOffered = payload.codeSignInOffered === true
  return {
    codeSignInOffered,
    // Never the code first where no code can be sent.
    primaryMethod:
      codeSignInOffered && payload.primaryMethod === "code" ? "code" : "password",
    selfRegistrationOpen: payload.selfRegistrationOpen !== false,
    passwordAtSignUp: payload.passwordAtSignUp !== false,
    nameAtSignUp: payload.nameAtSignUp !== false,
  }
}

/**
 * Last known branding, so a returning visitor's first frame is the real mark
 * rather than a placeholder.
 *
 * Legitimate here precisely because branding is cosmetic: a one-frame-stale
 * logo costs nothing, and the revalidation lands in the same second. The same
 * trick is forbidden for anything a user could act on or rely upon.
 */
function readCachedBranding(): BrandingPayload | undefined {
  try {
    const raw = window.localStorage.getItem(BRANDING_CACHE_KEY)
    return raw ? (JSON.parse(raw) as BrandingPayload) : undefined
  } catch {
    // Storage is absent in privacy-restricted contexts and in jsdom.
    return undefined
  }
}

function writeCachedBranding(value: BrandingPayload): void {
  try {
    window.localStorage.setItem(BRANDING_CACHE_KEY, JSON.stringify(value))
  } catch {
    // Losing the cache only costs the placeholder frame back.
  }
}

const BrandingContext = React.createContext<BrandingValue | undefined>(
  undefined
)

type ThemePreviewValue = {
  /** The appearance the platform has saved. */
  savedTheme: ThemeConfig
  /**
   * Shows an unsaved appearance on this page only, until replaced or cleared
   * with null. Nothing is cached and no other visitor sees it.
   */
  setThemePreview: (config: ThemeConfig | null) => void
}

const ThemePreviewContext = React.createContext<ThemePreviewValue | undefined>(
  undefined
)

/**
 * Computes and applies an appearance.
 *
 * The colour registry (~40 KB) is loaded only when there is something to
 * compute. The shipped preset needs nothing — preset.css already draws it —
 * and an appearance this browser has computed before is in the cache with
 * the key it was computed from. So the sign-in pages download the registry
 * once per appearance change, not once per visit, and never on an install
 * that keeps the default (`login-payload.spec.ts` measures that).
 */
function useAppliedTheme(config: ThemeConfig | null, cache: boolean) {
  // The key is the configuration itself, so the effect reads it back from the
  // key rather than from a value captured in render.
  const key = config ? themeConfigKey(config) : null

  React.useEffect(() => {
    if (key === null) return
    if (cache && key === DEFAULT_THEME_KEY) {
      applyThemeCss(null)
      writeCachedTheme(null)
      return
    }
    // The cached stylesheet is reusable only if this bundle would compute the
    // same one: same configuration, same engine.
    const cacheKey = `${THEME_ENGINE_VERSION}|${key}`
    const cached = cache ? readCachedTheme() : null
    if (cached?.key === cacheKey) {
      applyThemeCss(cached.css)
      return
    }
    const target = toThemeConfig(JSON.parse(key))
    let cancelled = false
    void import("@authsystem/ui/theme/build-theme").then(
      ({ buildThemeVars, themeCss }) => {
        if (cancelled) return
        const css = themeCss(buildThemeVars(target))
        applyThemeCss(css)
        if (cache) writeCachedTheme({ key: cacheKey, css })
      },
      () => {
        // The chunk failed to load (a deploy removed it, or the network
        // dropped): the cached or shipped colours stay, which is cosmetic.
      }
    )
    return () => {
      cancelled = true
    }
  }, [key, cache])
}

/** Uploads are served by the API host; resolve relative keys against it. */
function toAbsolute(url: string | null | undefined): string | null {
  if (!url) return null
  return url.startsWith("/") ? `${API_BASE_URL}${url}` : url
}

export { pickLogo }

/**
 * Fetches the public platform branding (anonymous endpoint) and applies it to
 * the browser tab (title + favicon). Screens read the name/logo via
 * `useBranding` — the sidebar header and the auth layout both do.
 */
export function BrandingProvider({ children }: { children: React.ReactNode }) {
  const { t } = useTranslation()
  const { resolvedTheme } = useTheme()

  const query = useQuery({
    queryKey: BRANDING_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Platform/branding")),
    staleTime: 5 * 60 * 1000,
    initialData: readCachedBranding,
    // The cache is of unknown age, so it seeds the first paint but must never
    // suppress the refetch: stamping it as already-stale makes this
    // show-cached-then-revalidate rather than trust-cached-for-five-minutes.
    initialDataUpdatedAt: 0,
  })

  const isPending = query.data === undefined
  const name = query.data?.platformName || t("common.appName")
  const logoUrl = pickLogo(
    toAbsolute(query.data?.logoUrl),
    toAbsolute(query.data?.logoUrlDark),
    resolvedTheme
  )
  // Dedicated square favicon when uploaded; wordmark logos are illegible at
  // tab-icon size, so admins can set a distinct mark. Falls back to the logo.
  const faviconUrl = toAbsolute(query.data?.faviconUrl) ?? logoUrl

  React.useEffect(() => {
    if (query.data) writeCachedBranding(query.data)
  }, [query.data])

  React.useEffect(() => {
    // While pending, `name` is the compiled-in product name — writing it made
    // the accounts tab read "Auth Console" for a beat. The document's own title
    // is the better placeholder: leave it alone until the truth arrives.
    if (isPending) return
    document.title = name
  }, [name, isPending])

  React.useEffect(() => {
    if (isPending) return
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) return
    if (faviconUrl) {
      // Uploaded images are raster (webp); drop the svg type of the default icon.
      link.removeAttribute("type")
      link.href = faviconUrl
    } else {
      link.type = "image/svg+xml"
      link.href = DEFAULT_FAVICON
    }
  }, [faviconUrl, isPending])

  const staySignedInOffered = query.data
    ? query.data.staySignedInOffered !== false
    : query.isError
      ? true
      : null

  // Keyed on the options themselves, not the payload object, so a refetch that
  // changes nothing keeps the same value (as the saved theme below).
  const signInOptionsKey = query.data
    ? JSON.stringify(toSignInOptions(query.data))
    : query.isError
      ? JSON.stringify(PASSWORD_ONLY)
      : null
  const signInOptions = React.useMemo<SignInOptions | null>(
    () => (signInOptionsKey ? JSON.parse(signInOptionsKey) : null),
    [signInOptionsKey]
  )

  const value = React.useMemo<BrandingValue>(
    () => ({ name, logoUrl, isPending, staySignedInOffered, signInOptions }),
    [name, logoUrl, isPending, staySignedInOffered, signInOptions]
  )

  // Unknown until the branding arrives: applying the default meanwhile would
  // replace a returning visitor's cached colours with the shipped ones.
  const savedThemeKey = query.data ? JSON.stringify(query.data.theme ?? null) : null
  const savedTheme = React.useMemo(
    () => toThemeConfig(savedThemeKey ? JSON.parse(savedThemeKey) : null),
    [savedThemeKey]
  )
  const [preview, setThemePreview] = React.useState<ThemeConfig | null>(null)
  useAppliedTheme(
    preview ?? (savedThemeKey === null ? null : savedTheme),
    preview === null
  )

  const previewValue = React.useMemo<ThemePreviewValue>(
    () => ({ savedTheme, setThemePreview }),
    [savedTheme]
  )

  return (
    <BrandingContext.Provider value={value}>
      <ThemePreviewContext.Provider value={previewValue}>
        {children}
      </ThemePreviewContext.Provider>
    </BrandingContext.Provider>
  )
}

/** The saved appearance, and a way to preview another one on this page. */
export function useThemePreview(): ThemePreviewValue {
  const context = React.useContext(ThemePreviewContext)
  if (!context) {
    throw new Error("useThemePreview must be used within a BrandingProvider")
  }
  return context
}

export function useBranding(): BrandingValue {
  const context = React.useContext(BrandingContext)
  if (!context) {
    throw new Error("useBranding must be used within a BrandingProvider")
  }
  return context
}

/**
 * Square platform logo mark: the uploaded logo when set, otherwise the given
 * fallback (the default shield icon), in a preset-styled tile.
 *
 * While the branding is still unknown neither is drawn. The fallback is a
 * statement — "this platform has no logo" — and asserting it before the answer
 * arrives is what made the default shield flash on every cold load. An empty
 * element carrying the same classes holds the height so the real mark does not
 * shove the page when it lands.
 */
export function BrandingLogo({
  fallback,
  className,
}: {
  /** Shown when no logo is uploaded; a function receives the platform's name. */
  fallback: React.ReactNode | ((name: string) => React.ReactNode)
  className?: string
}) {
  const { logoUrl, name, isPending } = useBranding()

  if (isPending) {
    return <div aria-hidden className={className ?? "size-8"} />
  }

  return logoUrl ? (
    <img
      src={logoUrl}
      alt={name}
      className={className ?? "size-8 rounded-lg object-cover"}
    />
  ) : (
    <>{typeof fallback === "function" ? fallback(name) : fallback}</>
  )
}

/**
 * The sign-in options alone (OI-215), for a page that only adjusts to them: null
 * while unknown, and outside a BrandingProvider, where the caller falls back to
 * what every deployment did before the options existed.
 */
export function useSignInOptions(): SignInOptions | null {
  return React.useContext(BrandingContext)?.signInOptions ?? null
}
