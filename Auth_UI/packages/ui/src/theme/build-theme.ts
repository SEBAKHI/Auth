/*
 * Turns the platform's appearance settings into CSS variables, by the same
 * merge https://ui.shadcn.com/create performs (`buildRegistryTheme` in
 * shadcn's apps/v4/registry/config.ts):
 *
 *   1. the base colour's full token set,
 *   2. overlaid by the theme's tokens (primary, secondary, sidebar-primary…),
 *   3. chart-1…5 replaced by the chart colour's,
 *   4. menu accent "bold" copies primary into accent,
 *   5. a non-default radius replaces --radius.
 *
 * Each of the three colours may instead be "custom": one colour per mode,
 * from which this file derives the tokens shadcn would otherwise supply.
 * The derivations keep shadcn's lightness ladders, so text contrast is what
 * the preset already measures; only hue and chroma come from the choice.
 */

import {
  clampChromaToSrgb,
  contrastRatio,
  formatOklch,
  hexToOklch,
  isHexColor,
  oklchToHex,
  parseOklch,
  type Oklch,
} from "./oklch"
import { THEMES } from "./shadcn-themes"
import {
  BASE_COLOR_NAMES,
  CUSTOM_PRESET,
  DEFAULT_THEME_CONFIG,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  type ColorChoice,
  type MenuAccent,
  type RadiusName,
  type ThemeConfig,
} from "./theme-config"

export type ColorMode = "light" | "dark"
export type ThemeVars = Record<string, string>
export type ThemeVarsByMode = Record<ColorMode, ThemeVars>

export {
  BASE_COLOR_NAMES,
  CUSTOM_PRESET,
  DEFAULT_THEME_CONFIG,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  type ColorChoice,
  type MenuAccent,
  type RadiusName,
  type ThemeConfig,
}

/** The colourful themes every base colour can be combined with. */
export const ACCENT_COLOR_NAMES = THEMES.map((theme) => theme.name).filter(
  (name) => !(BASE_COLOR_NAMES as readonly string[]).includes(name)
)

/**
 * The registry options of the Theme and Chart Color pickers for a base colour:
 * that base itself (a monochrome theme), then every colourful theme. The same
 * rule as shadcn's `getThemesForBaseColor`; the API enforces it too. A custom
 * base has no registry entry to be monochrome with, so it gets the colourful
 * themes only.
 */
export function themeNamesForBase(baseName: string): string[] {
  const base = (BASE_COLOR_NAMES as readonly string[]).includes(baseName)
    ? [baseName]
    : []
  return [...base, ...ACCENT_COLOR_NAMES]
}

function registryVars(name: string, mode: ColorMode): ThemeVars | undefined {
  const theme = THEMES.find((entry) => entry.name === name)
  return theme ? { ...theme.cssVars[mode] } : undefined
}

function customColor(choice: ColorChoice, mode: ColorMode): Oklch | null {
  const hex = mode === "light" ? choice.light : choice.dark
  return hex && isHexColor(hex) ? hexToOklch(hex) : null
}

function isBaseName(name: string): boolean {
  return (BASE_COLOR_NAMES as readonly string[]).includes(name)
}

function isCustom(choice: ColorChoice): boolean {
  return choice.preset === CUSTOM_PRESET
}

/**
 * The strongest tint any shadcn base colour carries (Mist and Taupe peak at
 * 0.034). Past it a "gray" stops reading as one, and muted text on a tinted
 * card starts losing the contrast the ladder was measured for.
 */
export const MAX_BASE_TINT = 0.035

/**
 * A tinted gray ladder in the style of Stone, Zinc or Mauve: Neutral's
 * lightness steps, the chosen hue, and a chroma that peaks mid-ladder and
 * fades to nothing at white and black — the shape shadcn's tinted bases have
 * (Stone: 0.001 at L 0.97, 0.013 at L 0.553, 0.004 at L 0.147).
 */
function tintedBase(tint: Oklch, mode: ColorMode): ThemeVars {
  const vars = registryVars("neutral", mode)!
  const strength = Math.min(tint.c, MAX_BASE_TINT)
  for (const [key, value] of Object.entries(vars)) {
    const color = parseOklch(value)
    // Only the gray steps: alpha borders, destructive and the blue
    // sidebar-primary of shadcn's dark Neutral are kept as shadcn wrote them.
    if (!color || color.c !== 0 || color.alpha !== undefined) continue
    const chroma = strength * 4 * color.l * (1 - color.l)
    vars[key] = formatOklch(clampChromaToSrgb({ l: color.l, c: chroma, h: tint.h }))
  }
  return vars
}

const LIGHT_INK: Oklch = { l: 0.985, c: 0, h: 0 }
const DARK_INK: Oklch = { l: 0.205, c: 0, h: 0 }

/**
 * Text on a custom primary: whichever of the preset's two inks reads better,
 * and pure white or black when neither reaches WCAG AA (4.5:1) — one of those
 * two always clears 4.58:1 against any colour.
 */
export function inkFor(background: Oklch): Oklch {
  const light = contrastRatio(background, LIGHT_INK)
  const dark = contrastRatio(background, DARK_INK)
  if (Math.max(light, dark) >= 4.5) return light >= dark ? LIGHT_INK : DARK_INK
  const white: Oklch = { l: 1, c: 0, h: 0 }
  const black: Oklch = { l: 0, c: 0, h: 0 }
  return contrastRatio(background, white) >= contrastRatio(background, black)
    ? white
    : black
}

/** The tokens a colourful shadcn theme sets, derived from one colour. */
function customThemeVars(primary: Oklch, mode: ColorMode): ThemeVars {
  // Every colourful shadcn theme carries the same zinc secondary; a custom
  // theme takes it too rather than inventing one.
  const shared = registryVars("blue", mode)!
  const color = clampChromaToSrgb(primary)
  const ink = formatOklch(inkFor(color))
  return {
    primary: formatOklch(color),
    "primary-foreground": ink,
    secondary: shared.secondary,
    "secondary-foreground": shared["secondary-foreground"],
    "sidebar-primary": formatOklch(color),
    "sidebar-primary-foreground": ink,
  }
}

/**
 * chart-1…5 lightness, light to dark: the average of shadcn's colourful
 * chart scales (chart-1 between 0.81 and 0.905, chart-5 between 0.45 and 0.48).
 */
export const CHART_LIGHTNESS = [0.865, 0.75, 0.645, 0.545, 0.465] as const

function customChartVars(color: Oklch): ThemeVars {
  const vars: ThemeVars = {}
  CHART_LIGHTNESS.forEach((l, index) => {
    vars[`chart-${index + 1}`] = formatOklch(
      clampChromaToSrgb({ l, c: color.c, h: color.h })
    )
  })
  return vars
}

function chartVars(source: ThemeVars): ThemeVars {
  const vars: ThemeVars = {}
  for (let i = 1; i <= 5; i++) {
    const key = `chart-${i}`
    if (source[key]) vars[key] = source[key]
  }
  return vars
}

function buildMode(config: ThemeConfig, mode: ColorMode): ThemeVars {
  const fallback = DEFAULT_THEME_CONFIG

  const baseTint = isCustom(config.base) ? customColor(config.base, mode) : null
  const vars: ThemeVars = baseTint
    ? tintedBase(baseTint, mode)
    : (registryVars(config.base.preset, mode) ??
      registryVars(fallback.base.preset, mode)!)

  const themeColor = isCustom(config.theme)
    ? customColor(config.theme, mode)
    : null
  // A monochrome theme is the base's own tokens: identical to the base for
  // the combinations the API accepts, and for any other (an old row, a
  // hand-edited one) overlaying another gray set would erase a custom tint.
  const monochrome = isBaseName(config.theme.preset)
  Object.assign(
    vars,
    themeColor
      ? customThemeVars(themeColor, mode)
      : monochrome
        ? {}
        : (registryVars(config.theme.preset, mode) ?? {})
  )

  const chartColor = isCustom(config.chart)
    ? customColor(config.chart, mode)
    : null
  // Gray charts on a custom base are the base's own, tinted with it.
  const keepBaseCharts = baseTint !== null && isBaseName(config.chart.preset)
  Object.assign(
    vars,
    chartColor
      ? customChartVars(chartColor)
      : keepBaseCharts
        ? {}
        : chartVars(
            registryVars(config.chart.preset, mode) ??
              registryVars(fallback.chart.preset, mode)!
          )
  )

  if (config.menuAccent === "bold") {
    vars.accent = vars.primary
    vars["accent-foreground"] = vars["primary-foreground"]
  }

  return vars
}

export function buildThemeVars(config: ThemeConfig): ThemeVarsByMode {
  const light = buildMode(config, "light")
  const dark = buildMode(config, "dark")

  const radius = RADIUS_OPTIONS.find((option) => option.name === config.radius)
  if (radius?.value) light.radius = radius.value
  // shadcn declares --radius once, on :root; dark inherits it.
  delete dark.radius

  return { light, dark }
}

const SAFE_KEY = /^[a-z0-9-]+$/
const SAFE_VALUE = /^[a-z0-9.%()/ #-]+$/i

function declarations(vars: ThemeVars): string {
  return Object.entries(vars)
    .filter(([key, value]) => SAFE_KEY.test(key) && SAFE_VALUE.test(value))
    .map(([key, value]) => `--${key}:${value};`)
    .join("")
}

/**
 * A stylesheet overriding preset.css. `html:root` / `html.dark` outrank the
 * preset's `:root` / `.dark` by specificity, so the result does not depend on
 * where the bundler happens to insert its own stylesheet. Every value comes
 * from the registry or from formatOklch; the allow-list filter is a second
 * line, so nothing that reaches this function can close the rule early.
 */
export function themeCss(vars: ThemeVarsByMode): string {
  return `html:root{${declarations(vars.light)}}html.dark{${declarations(vars.dark)}}`
}

/** shadcn's display name of a registry entry ("Neutral", "Amber"…). */
export function registryTitle(name: string): string {
  return THEMES.find((entry) => entry.name === name)?.title ?? name
}

/** The colour a picker shows next to a registry entry's name. */
export function swatchColor(
  name: string,
  role: "base" | "theme" | "chart",
  mode: ColorMode
): string | undefined {
  const vars = registryVars(name, mode)
  if (!vars) return undefined
  if (role === "base") return vars["muted-foreground"]
  if (role === "chart") return vars["chart-3"]
  return vars.primary
}

/**
 * The colour a custom picker starts from when the administrator switches a
 * choice to "custom": what that choice shows right now, so switching changes
 * nothing until a colour is actually picked.
 */
export function currentColorHex(
  config: ThemeConfig,
  role: "base" | "theme" | "chart",
  mode: ColorMode
): string {
  const vars = buildMode(config, mode)
  const token =
    role === "base" ? "muted-foreground" : role === "chart" ? "chart-3" : "primary"
  const parsed = parseOklch(vars[token] ?? "")
  return parsed ? oklchToHex(parsed) : "#737373"
}
