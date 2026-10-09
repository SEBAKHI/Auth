/*
 * The colour arithmetic the theme builder needs, and nothing more: sRGB hex
 * ⇄ OKLCH (Björn Ottosson's OKLab, the space every shadcn token is written
 * in), an sRGB gamut test, and the WCAG 2 contrast ratio.
 *
 * Hand-written rather than a colour library because the whole job is three
 * matrix products and a cube root; a library would add tens of kilobytes to
 * every sign-in page for functions this file covers in a hundred lines.
 */

export type Oklch = { l: number; c: number; h: number; alpha?: number }

const HEX_RE = /^#([0-9a-f]{6})$/i

/** True for `#rrggbb` — the only custom-colour format the API accepts. */
export function isHexColor(value: string): boolean {
  return HEX_RE.test(value)
}

function srgbToLinear(channel: number): number {
  return channel <= 0.04045
    ? channel / 12.92
    : Math.pow((channel + 0.055) / 1.055, 2.4)
}

function linearToSrgb(channel: number): number {
  return channel <= 0.0031308
    ? channel * 12.92
    : 1.055 * Math.pow(channel, 1 / 2.4) - 0.055
}

function linearRgbToOklch(r: number, g: number, b: number): Oklch {
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)

  const L = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s
  const A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s
  const B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s

  const c = Math.sqrt(A * A + B * B)
  // An achromatic colour has no meaningful hue; 0 is what shadcn writes.
  const h = c < 1e-4 ? 0 : (Math.atan2(B, A) * 180) / Math.PI
  return { l: L, c, h: h < 0 ? h + 360 : h }
}

/** Linear-light sRGB of an OKLCH colour; channels may fall outside [0, 1]. */
export function oklchToLinearRgb({ l, c, h }: Oklch): [number, number, number] {
  const hr = (h * Math.PI) / 180
  const A = c * Math.cos(hr)
  const B = c * Math.sin(hr)

  const l_ = Math.pow(l + 0.3963377774 * A + 0.2158037573 * B, 3)
  const m_ = Math.pow(l - 0.1055613458 * A - 0.0638541728 * B, 3)
  const s_ = Math.pow(l - 0.0894841775 * A - 1.291485548 * B, 3)

  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ]
}

export function hexToOklch(hex: string): Oklch {
  const match = HEX_RE.exec(hex)
  if (!match) throw new Error(`Not a #rrggbb colour: ${hex}`)
  const n = parseInt(match[1], 16)
  return linearRgbToOklch(
    srgbToLinear(((n >> 16) & 255) / 255),
    srgbToLinear(((n >> 8) & 255) / 255),
    srgbToLinear((n & 255) / 255)
  )
}

/** Nearest `#rrggbb` of an OKLCH colour (out-of-gamut channels are clipped). */
export function oklchToHex(color: Oklch): string {
  return (
    "#" +
    oklchToLinearRgb(color)
      .map((channel) => {
        const v = Math.round(
          Math.min(1, Math.max(0, linearToSrgb(Math.min(1, Math.max(0, channel))))) * 255
        )
        return v.toString(16).padStart(2, "0")
      })
      .join("")
  )
}

const OKLCH_RE =
  /^oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:\/\s*([\d.]+)(%?)\s*)?\)$/

/** Parses the `oklch(L C H)` / `oklch(L C H / A%)` strings shadcn writes. */
export function parseOklch(value: string): Oklch | null {
  const match = OKLCH_RE.exec(value.trim())
  if (!match) return null
  const color: Oklch = { l: Number(match[1]), c: Number(match[2]), h: Number(match[3]) }
  if (match[4] !== undefined) {
    color.alpha = Number(match[4]) / (match[5] ? 100 : 1)
  }
  return color
}

function round(value: number, digits: number): number {
  const factor = 10 ** digits
  return Math.round(value * factor) / factor
}

/** Writes an OKLCH colour the way shadcn does: three decimals, hue in degrees. */
export function formatOklch({ l, c, h, alpha }: Oklch): string {
  const body = `${round(l, 3)} ${round(c, 3)} ${c < 0.0005 ? 0 : round(h, 3)}`
  return alpha === undefined
    ? `oklch(${body})`
    : `oklch(${body} / ${round(alpha * 100, 1)}%)`
}

const GAMUT_EPSILON = 1e-6

export function isInSrgbGamut(color: Oklch): boolean {
  return oklchToLinearRgb(color).every(
    (channel) => channel >= -GAMUT_EPSILON && channel <= 1 + GAMUT_EPSILON
  )
}

/**
 * The colour itself when sRGB can show it, otherwise the same lightness and
 * hue at the highest chroma sRGB can show — never a lightness shift, which is
 * what contrast depends on.
 */
export function clampChromaToSrgb(color: Oklch): Oklch {
  if (isInSrgbGamut(color)) return color
  let low = 0
  let high = color.c
  // 20 halvings take a 0.4 chroma interval below 1e-6.
  for (let i = 0; i < 20; i++) {
    const mid = (low + high) / 2
    if (isInSrgbGamut({ ...color, c: mid })) low = mid
    else high = mid
  }
  return { ...color, c: low }
}

/** WCAG 2 relative luminance of an (in-gamut) OKLCH colour. */
export function relativeLuminance(color: Oklch): number {
  const [r, g, b] = oklchToLinearRgb(clampChromaToSrgb(color)).map((v) =>
    Math.min(1, Math.max(0, v))
  )
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** WCAG 2 contrast ratio, from 1 (none) to 21 (black on white). */
export function contrastRatio(a: Oklch, b: Oklch): number {
  const la = relativeLuminance(a)
  const lb = relativeLuminance(b)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}
