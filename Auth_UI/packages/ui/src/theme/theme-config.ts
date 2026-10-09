/*
 * The platform appearance as the API stores it: registry names, or "custom"
 * with a #rrggbb colour per mode. No colour data lives here, so anything that
 * only passes a configuration around (BrandingProvider) can import this
 * without pulling in the registry.
 */

export const CUSTOM_PRESET = "custom"

/**
 * The version of what build-theme.ts computes. Browsers cache the computed
 * stylesheet under the configuration it came from; a configuration that is
 * unchanged but computed differently by a newer bundle would otherwise keep
 * its old colours forever. Bump it whenever the output for an unchanged
 * configuration changes — `build-theme.test.ts` fails until you do.
 */
export const THEME_ENGINE_VERSION = 2

/** Base colours, in the order shadcn's picker lists them. */
export const BASE_COLOR_NAMES = [
  "neutral",
  "stone",
  "zinc",
  "mauve",
  "olive",
  "mist",
  "taupe",
] as const

export const RADIUS_OPTIONS = [
  { name: "default", value: "" },
  { name: "none", value: "0" },
  { name: "small", value: "0.45rem" },
  { name: "medium", value: "0.625rem" },
  { name: "large", value: "0.875rem" },
] as const

export type RadiusName = (typeof RADIUS_OPTIONS)[number]["name"]

export const MENU_ACCENT_OPTIONS = ["subtle", "bold"] as const
export type MenuAccent = (typeof MENU_ACCENT_OPTIONS)[number]

/** One of the three colour choices: a registry name, or custom per mode. */
export type ColorChoice = {
  preset: string
  /** `#rrggbb`; present exactly when `preset` is "custom". */
  light?: string | null
  dark?: string | null
}

export type ThemeConfig = {
  base: ColorChoice
  theme: ColorChoice
  chart: ColorChoice
  radius: RadiusName
  menuAccent: MenuAccent
}

/**
 * What `packages/ui/src/preset.css` renders: Neutral base and theme, Cyan
 * charts. A test holds the two equal, so an install that never opens the
 * appearance settings looks exactly as it did before they existed.
 */
export const DEFAULT_THEME_CONFIG: ThemeConfig = {
  base: { preset: "neutral" },
  theme: { preset: "neutral" },
  chart: { preset: "cyan" },
  radius: "default",
  menuAccent: "subtle",
}

/** A colour choice as the generated API types describe it: every member optional. */
type ColorChoiceDto = { preset?: string; light?: string | null; dark?: string | null }

type ThemeDto = {
  base?: ColorChoiceDto | null
  theme?: ColorChoiceDto | null
  chart?: ColorChoiceDto | null
  radius?: string | null
  menuAccent?: string | null
}

function choice(value: ColorChoiceDto | null | undefined, fallback: ColorChoice): ColorChoice {
  if (!value?.preset) return fallback
  return { preset: value.preset, light: value.light ?? null, dark: value.dark ?? null }
}

/**
 * The API's appearance as a ThemeConfig. Values outside the vocabulary (an
 * API newer than this bundle) fall back to the default for that setting
 * rather than failing the whole appearance.
 */
export function toThemeConfig(dto: ThemeDto | null | undefined): ThemeConfig {
  if (!dto) return DEFAULT_THEME_CONFIG
  const radius = RADIUS_OPTIONS.find((option) => option.name === dto.radius)
  const menuAccent = MENU_ACCENT_OPTIONS.find((option) => option === dto.menuAccent)
  return {
    base: choice(dto.base, DEFAULT_THEME_CONFIG.base),
    theme: choice(dto.theme, DEFAULT_THEME_CONFIG.theme),
    chart: choice(dto.chart, DEFAULT_THEME_CONFIG.chart),
    radius: radius?.name ?? DEFAULT_THEME_CONFIG.radius,
    menuAccent: menuAccent ?? DEFAULT_THEME_CONFIG.menuAccent,
  }
}

/**
 * The request body for saving a configuration: colours travel only with a
 * custom choice.
 */
export function toThemeRequest(config: ThemeConfig) {
  const body = (value: ColorChoice) =>
    value.preset === CUSTOM_PRESET
      ? { preset: value.preset, light: value.light ?? null, dark: value.dark ?? null }
      : { preset: value.preset, light: null, dark: null }
  return {
    base: body(config.base),
    theme: body(config.theme),
    chart: body(config.chart),
    radius: config.radius,
    menuAccent: config.menuAccent,
  }
}

/** Stable identity of a configuration, for effect dependencies and dirty checks. */
export function themeConfigKey(config: ThemeConfig): string {
  return JSON.stringify(toThemeRequest(config))
}
