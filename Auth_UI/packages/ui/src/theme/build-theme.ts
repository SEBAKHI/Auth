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
 * A custom base is taken literally (it is the page background); a custom
 * theme is the primary colour; a custom chart colour is spread over shadcn's
 * chart lightness ladder. Text on each is chosen for WCAG AA where any colour
 * allows it, and `baseReadability` reports a base where it does not.
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

const LIGHT_INK: Oklch = { l: 0.985, c: 0, h: 0 }
const DARK_INK: Oklch = { l: 0.205, c: 0, h: 0 }
const WHITE: Oklch = { l: 1, c: 0, h: 0 }
const BLACK: Oklch = { l: 0, c: 0, h: 0 }

/** WCAG AA for body text. */
const READABLE = 4.5

/**
 * Lightness steps of shadcn's Neutral, measured from the background toward
 * the text: how far each surface sits from the page in each scheme (light:
 * background 1, muted 0.97, border 0.922, ring 0.708; dark: background
 * 0.145, card 0.205, muted 0.269, ring 0.556).
 */
const STEPS = {
  light: { card: 0, sidebar: 0.015, muted: 0.03, border: 0.078, ring: 0.292 },
  dark: { card: 0.06, sidebar: 0.06, muted: 0.124, border: 0, ring: 0.411 },
} as const

/**
 * Text colour on a background: shadcn's ink (0.145 or 0.985) carrying a hint
 * of the background's hue, or pure black or white when the hint costs the
 * text its 4.5:1.
 */
function textOn(background: Oklch, dark: boolean): Oklch {
  const ink = clampChromaToSrgb({
    l: dark ? 0.145 : 0.985,
    c: Math.min(background.c, 0.02),
    h: background.h,
  })
  if (contrastRatio(background, ink) >= READABLE) return ink
  return dark ? BLACK : WHITE
}

/**
 * The base colour taken literally: the chosen colour IS the page background,
 * and every other surface keeps its distance from it on shadcn's own ladder
 * (cards, muted fills, borders, the sidebar), in the background's hue and
 * chroma. Text is dark or light by whichever reads better on that colour, so
 * a dark colour picked for light mode still gets light text.
 *
 * Muted text starts where shadcn puts it and moves toward the body text until
 * it reads at 4.5:1 on the muted fill. On a mid-tone background that can be
 * impossible; `baseReadability` reports it so the console can say so.
 */
function literalBase(background: Oklch, mode: ColorMode): ThemeVars {
  // Destructive, the chart grays and shadcn's sidebar-primary stay as the
  // registry writes them for this mode; the theme overrides the rest anyway.
  const vars = registryVars("neutral", mode)!
  const bg = clampChromaToSrgb(background)
  const darkText = contrastRatio(bg, BLACK) >= contrastRatio(bg, WHITE)
  const steps = darkText ? STEPS.light : STEPS.dark
  const direction = darkText ? -1 : 1
  const fg = textOn(bg, darkText)

  const surface = (step: number): Oklch =>
    clampChromaToSrgb({
      l: Math.min(1, Math.max(0, bg.l + direction * step)),
      c: bg.c,
      h: bg.h,
    })
  // A surface that text sits on. shadcn lifts dark-scheme cards toward the
  // text; on a mid-tone that would leave the text unreadable, so the step is
  // taken the other way when that reads better.
  const textSurface = (step: number): Oklch => {
    const toward = surface(step)
    if (contrastRatio(toward, fg) >= READABLE) return toward
    const away = surface(-step)
    return contrastRatio(away, fg) > contrastRatio(toward, fg) ? away : toward
  }
  const card = textSurface(steps.card)
  const muted = textSurface(steps.muted)

  // shadcn's muted text sits 52% (light) / 67% (dark) of the way from the
  // page to the body text; move further only as far as legibility needs.
  let share = darkText ? 0.52 : 0.67
  const between = (t: number): Oklch => ({
    l: bg.l + (fg.l - bg.l) * t,
    c: Math.min(bg.c, 0.02),
    h: bg.h,
  })
  while (share < 1 && contrastRatio(muted, between(share)) < READABLE) share += 0.02
  const mutedText = share >= 1 ? fg : clampChromaToSrgb(between(share))

  // Strong ink for primary surfaces: shadcn's 0.205 on light pages, 0.922 on
  // dark ones. A colourful or custom theme replaces both.
  const strong = clampChromaToSrgb({ l: darkText ? 0.205 : 0.922, c: Math.min(bg.c, 0.02), h: bg.h })
  const onStrong = textOn(strong, !darkText)

  const border = darkText
    ? formatOklch(surface(steps.border))
    : formatOklch({ ...fg, alpha: 0.1 })
  const input = darkText ? border : formatOklch({ ...fg, alpha: 0.15 })
  const ring = formatOklch(surface(steps.ring))
  const text = formatOklch(fg)

  return {
    ...vars,
    background: formatOklch(bg),
    foreground: text,
    card: formatOklch(card),
    "card-foreground": text,
    popover: formatOklch(card),
    "popover-foreground": text,
    primary: formatOklch(strong),
    "primary-foreground": formatOklch(onStrong),
    secondary: formatOklch(muted),
    "secondary-foreground": text,
    muted: formatOklch(muted),
    "muted-foreground": formatOklch(mutedText),
    accent: formatOklch(muted),
    "accent-foreground": text,
    border,
    input,
    ring,
    sidebar: formatOklch(textSurface(steps.sidebar)),
    "sidebar-foreground": text,
    "sidebar-accent": formatOklch(muted),
    "sidebar-accent-foreground": text,
    "sidebar-border": border,
    "sidebar-ring": ring,
  }
}

/**
 * How well text reads on a custom base colour: the weakest of body text on
 * the page, on cards and on muted fills, and of muted text on muted fills. Below 4.5:1 the
 * console warns before saving; it does not refuse, because the colour is the
 * administrator's to choose.
 */
export function baseReadability(hex: string, mode: ColorMode): number {
  if (!isHexColor(hex)) return 21
  const vars = literalBase(hexToOklch(hex), mode)
  const read = (key: string) => parseOklch(vars[key])!
  return Math.min(
    contrastRatio(read("background"), read("foreground")),
    contrastRatio(read("card"), read("card-foreground")),
    contrastRatio(read("muted"), read("accent-foreground")),
    contrastRatio(read("muted"), read("muted-foreground"))
  )
}

/**
 * Text on a custom primary: whichever of the preset's two inks reads better,
 * and pure white or black when neither reaches WCAG AA (4.5:1) — one of those
 * two always clears 4.58:1 against any colour.
 */
export function inkFor(background: Oklch): Oklch {
  const light = contrastRatio(background, LIGHT_INK)
  const dark = contrastRatio(background, DARK_INK)
  if (Math.max(light, dark) >= READABLE) return light >= dark ? LIGHT_INK : DARK_INK
  return contrastRatio(background, WHITE) >= contrastRatio(background, BLACK)
    ? WHITE
    : BLACK
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

  const baseColor = isCustom(config.base) ? customColor(config.base, mode) : null
  const vars: ThemeVars = baseColor
    ? literalBase(baseColor, mode)
    : (registryVars(config.base.preset, mode) ??
      registryVars(fallback.base.preset, mode)!)

  const themeColor = isCustom(config.theme)
    ? customColor(config.theme, mode)
    : null
  // A monochrome theme is the base's own tokens: identical to the base for
  // the combinations the API accepts, and for any other (an old row, a
  // hand-edited one) overlaying another gray set would erase a custom base.
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
  // Gray charts on a custom base are Neutral's (an old or hand-edited row:
  // the API refuses a monochrome chart colour on a custom base).
  const keepBaseCharts = baseColor !== null && isBaseName(config.chart.preset)
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
 * where the bundler happens to insert its own stylesheet. `html .light` and
 * `html .dark` carry the same values to an element that shows one mode inside
 * a page in the other (preset.css scopes its tokens the same way). Every value comes
 * from the registry or from formatOklch; the allow-list filter is a second
 * line, so nothing that reaches this function can close the rule early.
 */
export function themeCss(vars: ThemeVarsByMode): string {
  return `html:root,html .light{${declarations(vars.light)}}html.dark,html .dark{${declarations(vars.dark)}}`
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
  // A custom base IS the page background, so that is what it starts from.
  const token =
    role === "base" ? "background" : role === "chart" ? "chart-3" : "primary"
  const parsed = parseOklch(vars[token] ?? "")
  return parsed ? oklchToHex(parsed) : "#737373"
}
