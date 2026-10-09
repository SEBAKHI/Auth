import { createHash } from "node:crypto"
import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import {
  ACCENT_COLOR_NAMES,
  BASE_COLOR_NAMES,
  CHART_LIGHTNESS,
  DEFAULT_THEME_CONFIG,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  baseReadability,
  buildThemeVars,
  currentColorHex,
  inkFor,
  themeCss,
  themeNamesForBase,
  type ThemeConfig,
} from "./build-theme"
import { contrastRatio, hexToOklch, oklchToHex, parseOklch } from "./oklch"
import { THEMES } from "./shadcn-themes"
import { THEME_ENGINE_VERSION } from "./theme-config"

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
  // `:root` is declared together with `.light` (`:root,\n.light {`).
  const pattern = selector === ":root" ? "^:root,\\s*\\.light \\{" : "^\\.dark \\{"
  const start = css.search(new RegExp(pattern, "m"))
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
  it("makes the chosen colour the page background, exactly", () => {
    const { light, dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: custom("#1e3a8a", "#fef3c7"),
      theme: { preset: "blue" },
    })
    expect(oklchToHex(parseOklch(light.background)!)).toBe("#1e3a8a")
    expect(oklchToHex(parseOklch(dark.background)!)).toBe("#fef3c7")
  })

  it("lays shadcn's own ladder on white, so white reproduces Neutral's surfaces", () => {
    const { light } = buildThemeVars({ ...DEFAULT_THEME_CONFIG, base: custom("#ffffff", "#0a0a0a") })
    const neutral = presetBlock(":root")
    for (const key of ["background", "foreground", "card", "muted", "accent", "border", "input", "ring", "sidebar"]) {
      expect(light[key], key).toBe(neutral[key])
    }
  })

  it("chooses the text by the colour, not by the mode", () => {
    // Navy picked for light mode still gets light text; cream picked for
    // dark mode gets dark text.
    const { light, dark } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      base: custom("#1e3a8a", "#fef3c7"),
    })
    expect(parseOklch(light.foreground)!.l).toBeGreaterThan(0.5)
    expect(parseOklch(dark.foreground)!.l).toBeLessThan(0.5)
  })

  it.each(["#ffffff", "#000000", "#1e3a8a", "#fef3c7", "#ff0000", "#00ff88", "#7c3aed", "#808080"])(
    "keeps body text on %s readable (WCAG AA) on the page and on cards",
    (hex) => {
      for (const mode of ["light", "dark"] as const) {
        const vars = buildThemeVars({ ...DEFAULT_THEME_CONFIG, base: custom(hex, hex) })[mode]
        const pair = (bg: string, fg: string) =>
          contrastRatio(parseOklch(vars[bg])!, parseOklch(vars[fg])!)
        expect(pair("background", "foreground"), `${mode} page`).toBeGreaterThanOrEqual(4.5)
        expect(pair("card", "card-foreground"), `${mode} card`).toBeGreaterThanOrEqual(4.5)
        expect(pair("muted", "accent-foreground"), `${mode} hover`).toBeGreaterThanOrEqual(4.5)
      }
    }
  )

  it("reports how well text reads, so a weak colour can be flagged before saving", () => {
    expect(baseReadability("#ffffff", "light")).toBeGreaterThanOrEqual(4.5)
    expect(baseReadability("#1e3a8a", "light")).toBeGreaterThanOrEqual(4.5)
    // A mid-tone leaves muted text nowhere to go: it is reported, not hidden.
    const midTone = baseReadability("#8a8a8a", "light")
    const vars = buildThemeVars({ ...DEFAULT_THEME_CONFIG, base: custom("#8a8a8a", "#8a8a8a") }).light
    expect(midTone).toBeCloseTo(
      Math.min(
        contrastRatio(parseOklch(vars.background)!, parseOklch(vars.foreground)!),
        contrastRatio(parseOklch(vars.card)!, parseOklch(vars["card-foreground"])!),
        contrastRatio(parseOklch(vars.muted)!, parseOklch(vars["accent-foreground"])!),
        contrastRatio(parseOklch(vars.muted)!, parseOklch(vars["muted-foreground"])!)
      ),
      5
    )
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
    expect(css.startsWith("html:root,html .light{--background:oklch(1 0 0);")).toBe(true)
    expect(css).toContain("}html.dark,html .dark{--background:oklch(0.145 0 0);")
  })

  it("drops anything that could close the rule", () => {
    const css = themeCss({
      light: { primary: "red}body{display:none", "x;y": "red", ok: "oklch(0.5 0.1 20)" },
      dark: {},
    })
    expect(css).toBe("html:root,html .light{--ok:oklch(0.5 0.1 20);}html.dark,html .dark{}")
  })
})

describe("the engine version", () => {
  // Browsers reuse a stylesheet computed for the same configuration and the
  // same THEME_ENGINE_VERSION. If this fingerprint moves, what the engine
  // computes moved: bump THEME_ENGINE_VERSION (theme-config.ts) and record
  // both new values here, or visitors keep the colours of the old engine.
  const RECORDED = {
    version: 3,
    fingerprint: "cc0a9bf2dc12373f",
  }

  it("changes whenever the computed stylesheets change", () => {
    const samples: ThemeConfig[] = [
      DEFAULT_THEME_CONFIG,
      ...BASE_COLOR_NAMES.flatMap((base) =>
        themeNamesForBase(base).map((name) => ({
          ...DEFAULT_THEME_CONFIG,
          base: { preset: base },
          theme: { preset: name },
          chart: { preset: name },
        }))
      ),
      ...["#ffffff", "#000000", "#1e3a8a", "#fef3c7", "#8a8a8a", "#7c3aed"].map((hex) => ({
        base: custom(hex, hex),
        theme: custom(hex, hex),
        chart: custom(hex, hex),
        radius: "large" as const,
        menuAccent: "bold" as const,
      })),
    ]
    const fingerprint = createHash("sha256")
      .update(samples.map((config) => themeCss(buildThemeVars(config))).join("\n"))
      .digest("hex")
      .slice(0, 16)

    expect({ version: THEME_ENGINE_VERSION, fingerprint }).toEqual(RECORDED)
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
