import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import {
  ACCENT_COLOR_NAMES,
  BASE_COLOR_NAMES,
  CHART_LIGHTNESS,
  DEFAULT_THEME_CONFIG,
  MAX_BASE_TINT,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  buildThemeVars,
  currentColorHex,
  inkFor,
  themeCss,
  themeNamesForBase,
  type ThemeConfig,
} from "./build-theme"
import { contrastRatio, hexToOklch, parseOklch } from "./oklch"
import { THEMES } from "./shadcn-themes"

function repoFile(relative: string): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, relative)
    if (existsSync(candidate)) {
      return readFileSync(candidate, "utf8")
        .replace(/^\uFEFF/, "")
        .replace(/\r\n/g, "\n")
    }
    dir = dirname(dir)
  }
  throw new Error(`${relative} not found above ${process.cwd()}`)
}

/** The `--name: value;` declarations of one top-level rule of preset.css. */
function presetBlock(selector: ":root" | ".dark"): Record<string, string> {
  const css = repoFile("Auth_UI/packages/ui/src/preset.css")
  const start = css.search(new RegExp(`^${selector.replace(".", "\\.")} \\{`, "m"))
  expect(start, `${selector} block in preset.css`).toBeGreaterThanOrEqual(0)
  const body = css.slice(start, css.indexOf("}", start))
  const vars: Record<string, string> = {}
  for (const match of body.matchAll(/--([a-z0-9-]+):\s*([^;]+);/g)) {
    vars[match[1]] = match[2].trim()
  }
  return vars
}

const custom = (light: string, dark: string) => ({
  preset: "custom",
  light,
  dark,
})

describe("buildThemeVars — the shadcn merge", () => {
  it("renders exactly preset.css for the default settings", () => {
    // The shipped look and the "nobody has customised anything" settings are
    // one decision written twice. This is the test that keeps them one.
    const { light, dark } = buildThemeVars(DEFAULT_THEME_CONFIG)
    const root = presetBlock(":root")
    const darkBlock = presetBlock(".dark")

    expect(Object.keys(root).length).toBeGreaterThan(30)
    expect(light).toEqual(root)
    expect(dark).toEqual(darkBlock)
  })

  it("overlays the theme on the base and the chart colour on both", () => {
    const { light, dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: { preset: "stone" },
      theme: { preset: "amber" },
      chart: { preset: "rose" },
    })
    const stone = THEMES.find((t) => t.name === "stone")!.cssVars
    const amber = THEMES.find((t) => t.name === "amber")!.cssVars
    const rose = THEMES.find((t) => t.name === "rose")!.cssVars

    expect(light.background).toBe(stone.light.background)
    expect(light.border).toBe(stone.light.border)
    expect(light.primary).toBe(amber.light.primary)
    expect(dark.primary).toBe(amber.dark.primary)
    expect(dark["sidebar-primary"]).toBe(amber.dark["sidebar-primary"])
    expect(light["chart-1"]).toBe(rose.light["chart-1"])
    expect(dark["chart-5"]).toBe(rose.dark["chart-5"])
    // A chart colour lends its charts only, never its primary or sidebar.
    expect(dark.sidebar).toBe(stone.dark.sidebar)
  })

  it("copies primary into accent for the bold menu accent", () => {
    const { light, dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      theme: { preset: "blue" },
      menuAccent: "bold",
    })
    expect(light.accent).toBe(light.primary)
    expect(light["accent-foreground"]).toBe(light["primary-foreground"])
    expect(dark.accent).toBe(dark.primary)
  })

  it("writes a non-default radius once, on the light (root) block", () => {
    const { light, dark } = buildThemeVars({ ...DEFAULT_THEME_CONFIG, radius: "large" })
    expect(light.radius).toBe("0.875rem")
    expect(dark.radius).toBeUndefined()
    expect(buildThemeVars({ ...DEFAULT_THEME_CONFIG, radius: "none" }).light.radius).toBe("0")
  })

  it("falls back to the default base for an unknown name rather than rendering nothing", () => {
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: { preset: "not-a-colour" },
    })
    expect(light.background).toBe("oklch(1 0 0)")
  })
})

describe("custom colours", () => {
  it("tints Neutral's gray ladder without moving its lightness", () => {
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: custom("#3b82f6", "#3b82f6"),
    })
    const neutral = presetBlock(":root")
    for (const key of ["foreground", "muted-foreground", "border", "secondary"]) {
      const tinted = parseOklch(light[key])!
      expect(tinted.l, key).toBeCloseTo(parseOklch(neutral[key])!.l, 3)
      expect(tinted.c, key).toBeGreaterThan(0)
      expect(tinted.c, key).toBeLessThanOrEqual(MAX_BASE_TINT)
    }
    // White stays white, as on every shadcn base.
    expect(light.background).toBe("oklch(1 0 0)")
    expect(light.destructive).toBe(neutral.destructive)
  })

  it("caps a saturated base pick at the strongest shadcn tint", () => {
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: custom("#ff0000", "#ff0000"),
    })
    const grays = Object.entries(light).filter(
      ([key]) => !["destructive", "radius", "sidebar-primary"].includes(key) && !key.startsWith("chart-")
    )
    expect(grays.length).toBeGreaterThan(20)
    for (const [key, value] of grays) {
      expect(parseOklch(value)!.c, key).toBeLessThanOrEqual(MAX_BASE_TINT)
    }
  })

  it("uses each mode's own pick", () => {
    const { light, dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      theme: custom("#2563eb", "#f59e0b"),
    })
    expect(parseOklch(light.primary)!.h).toBeCloseTo(hexToOklch("#2563eb").h, 0)
    expect(parseOklch(dark.primary)!.h).toBeCloseTo(hexToOklch("#f59e0b").h, 0)
    expect(light["sidebar-primary"]).toBe(light.primary)
  })

  it.each(["#ffff00", "#777777", "#000000", "#ffffff", "#00ff88", "#1e3a8a"])(
    "gives a custom primary %s readable text (WCAG AA)",
    (hex) => {
      const { light } = buildThemeVars({
        ...DEFAULT_THEME_CONFIG,
        theme: custom(hex, hex),
      })
      expect(
        contrastRatio(parseOklch(light.primary)!, parseOklch(light["primary-foreground"])!)
      ).toBeGreaterThanOrEqual(4.5)
    }
  )

  it("derives five chart shades from one colour, light to dark", () => {
    const { dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      chart: custom("#16a34a", "#16a34a"),
    })
    const shades = [1, 2, 3, 4, 5].map((i) => parseOklch(dark[`chart-${i}`])!)
    shades.forEach((shade, index) => {
      expect(shade.l).toBeCloseTo(CHART_LIGHTNESS[index], 3)
      expect(shade.h).toBeCloseTo(hexToOklch("#16a34a").h, 0)
    })
  })

  it("ignores a custom choice that lost its colour instead of rendering a hole", () => {
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      theme: { preset: "custom", light: null, dark: null },
    })
    expect(light.primary).toBe(presetBlock(":root").primary)
  })

  it("seeds a switch to custom with the colour on screen", () => {
    const config: ThemeConfig = { ...DEFAULT_THEME_CONFIG, theme: { preset: "blue" } }
    const seeded = currentColorHex(config, "theme", "light")
    const after = buildThemeVars({ ...config, theme: custom(seeded, seeded) })
    const before = buildThemeVars(config)
    expect(parseOklch(after.light.primary)!.l).toBeCloseTo(parseOklch(before.light.primary)!.l, 2)
  })
})

describe("inkFor", () => {
  it("picks dark text on a light colour and light text on a dark one", () => {
    expect(inkFor({ l: 0.95, c: 0.05, h: 100 }).l).toBeLessThan(0.5)
    expect(inkFor({ l: 0.3, c: 0.1, h: 260 }).l).toBeGreaterThan(0.5)
  })
})

describe("themeCss", () => {
  it("outranks preset.css by specificity and carries both modes", () => {
    const css = themeCss(buildThemeVars(DEFAULT_THEME_CONFIG))
    expect(css.startsWith("html:root{--background:oklch(1 0 0);")).toBe(true)
    expect(css).toContain("}html.dark{--background:oklch(0.145 0 0);")
  })

  it("drops anything that could close the rule", () => {
    const css = themeCss({
      light: { primary: "red}body{display:none", "x;y": "red", ok: "oklch(0.5 0.1 20)" },
      dark: {},
    })
    expect(css).toBe("html:root{--ok:oklch(0.5 0.1 20);}html.dark{}")
  })
})

describe("the API's vocabulary", () => {
  // ThemePresets.cs is the list the API validates against; the registry copy
  // is what the console renders. A name only one side knows is either a
  // setting the API refuses or one the console silently draws as the default.
  const source = repoFile("Auth/Auth.Domain/Constants/ThemePresets.cs")

  function csharpList(name: string): string[] {
    const match = new RegExp(`${name} =\\s*\\[([^\\]]*)\\]`).exec(source)
    expect(match, `${name} in ThemePresets.cs`).not.toBeNull()
    return [...match![1].matchAll(/"([a-z-]+)"/g)].map((m) => m[1])
  }

  it("names the same base colours, in the same order", () => {
    expect(csharpList("BaseColors")).toEqual([...BASE_COLOR_NAMES])
  })

  it("names the same colourful themes, in the same order", () => {
    expect(csharpList("AccentColors")).toEqual(ACCENT_COLOR_NAMES)
    expect(ACCENT_COLOR_NAMES).toHaveLength(17)
  })

  it("names the same radii and menu accents", () => {
    expect(csharpList("Radii")).toEqual(RADIUS_OPTIONS.map((r) => r.name))
    expect(csharpList("MenuAccents")).toEqual([...MENU_ACCENT_OPTIONS])
  })

  it("has the same defaults", () => {
    const constant = (name: string) =>
      new RegExp(`${name} = "([a-z-]+)"`).exec(source)?.[1]
    expect(constant("DefaultBaseColor")).toBe(DEFAULT_THEME_CONFIG.base.preset)
    expect(constant("DefaultTheme")).toBe(DEFAULT_THEME_CONFIG.theme.preset)
    expect(constant("DefaultChartColor")).toBe(DEFAULT_THEME_CONFIG.chart.preset)
    expect(constant("DefaultRadius")).toBe(DEFAULT_THEME_CONFIG.radius)
    expect(constant("DefaultMenuAccent")).toBe(DEFAULT_THEME_CONFIG.menuAccent)
  })

  it("offers a base itself and the colourful themes, as shadcn does", () => {
    expect(themeNamesForBase("stone")).toEqual(["stone", ...ACCENT_COLOR_NAMES])
    expect(themeNamesForBase("custom")).toEqual(ACCENT_COLOR_NAMES)
  })
})
