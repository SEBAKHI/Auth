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
export const THEME_ENGINE_VERSION = 4

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

/** The steps of the control height and the table row height, smallest first. */
export const SIZE_STEPS = ["compact", "default", "comfortable"] as const
export type SizeStep = (typeof SIZE_STEPS)[number]

/** A height scale: pixels per step, and the bounds of a custom height. */
export type SizeScale = {
  steps: Readonly<Record<SizeStep, number>>
  min: number
  max: number
}

/**
 * Every control: buttons, inputs, selects, input groups, toggles, tabs.
 * "default" is the preset's own `h-9`.
 */
export const CONTROL_HEIGHT: SizeScale = {
  steps: { compact: 32, default: 36, comfortable: 40 },
  min: 28,
  max: 56,
}

/**
 * A table row holding one line of text. "default" is the preset's own `p-3`
 * around a 20px line; a row holding a taller thing (an avatar, a button)
 * grows by the difference, as it always has. The smallest step is no lower
 * than the tallest control step, so any two steps go together; a custom row
 * is never lower than the controls (the API refuses it).
 */
export const TABLE_ROW_HEIGHT: SizeScale = {
  steps: { compact: 40, default: 44, comfortable: 48 },
  min: 32,
  max: 72,
}

/** One of the two size choices: a step, or custom with a height in pixels. */
export type SizeChoice = {
  preset: SizeStep | typeof CUSTOM_PRESET
  /** Whole pixels; present exactly when `preset` is "custom". */
  px?: number | null
}

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
  controlSize: SizeChoice
  tableRowHeight: SizeChoice
}

/**
 * What `packages/ui/src/preset.css` renders: Neutral base and theme, Cyan
 * charts, controls at `h-9`, table rows at 44px. A test holds the two equal,
 * so an install that never opens the appearance settings looks exactly as it
 * did before they existed.
 */
export const DEFAULT_THEME_CONFIG: ThemeConfig = {
  base: { preset: "neutral" },
  theme: { preset: "neutral" },
  chart: { preset: "cyan" },
  radius: "default",
  menuAccent: "subtle",
  controlSize: { preset: "default" },
  tableRowHeight: { preset: "default" },
}

/**
 * Whether a custom height can be drawn on a scale: whole pixels within its
 * bounds. Anything else (a field being typed, an API newer than this bundle)
 * draws as the default step instead.
 */
export function isValidCustomPx(px: unknown, scale: SizeScale): px is number {
  return typeof px === "number" && Number.isInteger(px) && px >= scale.min && px <= scale.max
}

/** The height in pixels a size choice draws at. */
export function sizePx(choice: SizeChoice, scale: SizeScale): number {
  if (choice.preset === CUSTOM_PRESET) {
    return isValidCustomPx(choice.px, scale) ? choice.px : scale.steps.default
  }
  return scale.steps[choice.preset] ?? scale.steps.default
}

/** A colour choice as the generated API types describe it: every member optional. */
type ColorChoiceDto = { preset?: string; light?: string | null; dark?: string | null }

/** A size choice as the generated API types describe it (int32 may arrive as a string). */
type SizeChoiceDto = { preset?: string; px?: number | string | null }

type ThemeDto = {
  base?: ColorChoiceDto | null
  theme?: ColorChoiceDto | null
  chart?: ColorChoiceDto | null
  radius?: string | null
  menuAccent?: string | null
  controlSize?: SizeChoiceDto | null
  tableRowHeight?: SizeChoiceDto | null
}

function choice(value: ColorChoiceDto | null | undefined, fallback: ColorChoice): ColorChoice {
  if (!value?.preset) return fallback
  return { preset: value.preset, light: value.light ?? null, dark: value.dark ?? null }
}

function size(value: SizeChoiceDto | null | undefined, scale: SizeScale): SizeChoice {
  const step = SIZE_STEPS.find((name) => name === value?.preset)
  if (step) return { preset: step }
  if (value?.preset === CUSTOM_PRESET) {
    const px = typeof value.px === "string" ? Number(value.px) : value.px
    if (isValidCustomPx(px, scale)) return { preset: CUSTOM_PRESET, px }
  }
  // Absent (an API older than this bundle) or unknown: the preset's own height.
  return { preset: "default" }
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
    controlSize: size(dto.controlSize, CONTROL_HEIGHT),
    tableRowHeight: size(dto.tableRowHeight, TABLE_ROW_HEIGHT),
  }
}

/**
 * The request body for saving a configuration: colours travel only with a
 * custom colour choice, pixels only with a custom size.
 */
export function toThemeRequest(config: ThemeConfig) {
  const body = (value: ColorChoice) =>
    value.preset === CUSTOM_PRESET
      ? { preset: value.preset, light: value.light ?? null, dark: value.dark ?? null }
      : { preset: value.preset, light: null, dark: null }
  const sizeBody = (value: SizeChoice) => ({
    preset: value.preset,
    px: value.preset === CUSTOM_PRESET ? (value.px ?? null) : null,
  })
  return {
    base: body(config.base),
    theme: body(config.theme),
    chart: body(config.chart),
    radius: config.radius,
    menuAccent: config.menuAccent,
    controlSize: sizeBody(config.controlSize),
    tableRowHeight: sizeBody(config.tableRowHeight),
  }
}

/** Stable identity of a configuration, for effect dependencies and dirty checks. */
export function themeConfigKey(config: ThemeConfig): string {
  return JSON.stringify(toThemeRequest(config))
}
