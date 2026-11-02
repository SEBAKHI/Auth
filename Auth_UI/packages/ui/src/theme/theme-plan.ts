import {
  applyThemeCss,
  readCachedTheme,
  writeCachedTheme,
  type ThemeScope,
} from "@authsystem/ui/theme/apply-theme"
import {
  DEFAULT_THEME_CONFIG,
  THEME_ENGINE_VERSION,
  themeConfigKey,
  type ThemeConfig,
} from "@authsystem/ui/theme/theme-config"

/*
 * How an appearance reaches the page, apart from BrandingProvider so a page that
 * must not load the API client (the account bar's frames) applies it the same way.
 */

const DEFAULT_THEME_KEY = themeConfigKey(DEFAULT_THEME_CONFIG)

/**
 * What the page should look like:
 * - `keep`: nothing is known yet; what is on the page stays (the first frame's
 *   cached colours, or the shipped preset).
 * - `apply`: an appearance, cached under `scope` once computed; `scope`
 *   undefined caches nothing (an administrator's preview).
 * - `cached`: an application's own appearance that is not known yet: its
 *   cached copy if this browser has one, otherwise `fallback`.
 */
export type ThemePlan =
  | { kind: "keep" }
  | { kind: "apply"; config: ThemeConfig; scope?: ThemeScope }
  | { kind: "cached"; scope: string; fallback: ThemePlan }

export function applyPlan(plan: ThemePlan): (() => void) | undefined {
  if (plan.kind === "keep") return undefined
  if (plan.kind === "cached") {
    const cached = readCachedTheme(plan.scope)
    if (!cached) return applyPlan(plan.fallback)
    applyThemeCss(cached.css || null)
    return undefined
  }

  const { scope } = plan
  const cache = scope !== undefined
  const key = themeConfigKey(plan.config)
  // The cached stylesheet is reusable only if this bundle would compute the
  // same one: same configuration, same engine.
  const cacheKey = `${THEME_ENGINE_VERSION}|${key}`
  if (key === DEFAULT_THEME_KEY) {
    applyThemeCss(null)
    // The platform on the preset needs no entry; an application on it does,
    // or its next first frame would show the platform's colours instead.
    if (cache) writeCachedTheme(scope === null ? null : { key: cacheKey, css: "" }, scope)
    return undefined
  }
  const cached = cache ? readCachedTheme(scope) : null
  if (cached?.key === cacheKey) {
    applyThemeCss(cached.css)
    // Used again: the last to be forgotten among the applications kept.
    if (scope) writeCachedTheme(cached, scope)
    return undefined
  }
  let cancelled = false
  void import("@authsystem/ui/theme/build-theme").then(
    ({ buildThemeVars, themeCss }) => {
      if (cancelled) return
      const css = themeCss(buildThemeVars(plan.config))
      applyThemeCss(css)
      if (cache) writeCachedTheme({ key: cacheKey, css }, scope)
    },
    () => {
      // The chunk failed to load (a deploy removed it, or the network
      // dropped): the cached or shipped colours stay, which is cosmetic.
    }
  )
  return () => {
    cancelled = true
  }
}
