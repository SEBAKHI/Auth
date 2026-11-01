import { createHash } from "node:crypto"
import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import {
  ACCENT_COLOR_NAMES,
  BASE_COLOR_NAMES,
  CHART_LIGHTNESS,
  CONTROL_HEIGHT,
  DEFAULT_THEME_CONFIG,
  HEADER_ICON_SIZES,
  HEADER_ICON_SIZE_PX,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  SIZE_STEPS,
  TABLE_ROW_HEIGHT,
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
import { HEADER_ICON_SIZE_PHONE_CAP, THEME_ENGINE_VERSION } from "./theme-config"

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

  it.each(SIZE_STEPS.map((step) => [step]))(
    "writes the %s control and row heights once, on the light (root) block",
    (step) => {
      const { light, dark } = buildThemeVars({
        ...DEFAULT_THEME_CONFIG,
        controlSize: { preset: step },
        tableRowHeight: { preset: step },
      })
      expect(light["control-height"]).toBe(`${CONTROL_HEIGHT.steps[step] / 16}rem`)
      expect(light["table-row-height"]).toBe(`${TABLE_ROW_HEIGHT.steps[step] / 16}rem`)
      expect(dark["control-height"]).toBeUndefined()
      expect(dark["table-row-height"]).toBeUndefined()
    }
  )

  it("writes a custom height in rem, exactly", () => {
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      controlSize: { preset: "custom", px: 30 },
      tableRowHeight: { preset: "custom", px: 61 },
    })
    expect(light["control-height"]).toBe("1.875rem")
    expect(light["table-row-height"]).toBe("3.8125rem")
  })

  it.each([
    { px: null },
    { px: 27 },
    { px: 57 },
    { px: 33.5 },
  ])("draws a custom control height it cannot use ($px) as the default", ({ px }) => {
    // A number field being typed passes through here on its way to valid.
    const { light } = buildThemeVars({
      ...DEFAULT_THEME_CONFIG,
      controlSize: { preset: "custom", px },
    })
    expect(light["control-height"]).toBe("2.25rem")
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

describe("the header icon size", () => {
  const rem = (px: number) => `${px / 16}rem`

  it("writes the icon, button and avatar of each size once, on the light (root) block", () => {
    for (const size of HEADER_ICON_SIZES) {
      const { light, dark } = buildThemeVars({ ...DEFAULT_THEME_CONFIG, headerIconSize: size })
      const px = HEADER_ICON_SIZE_PX[size]
      expect(light["header-icon"], size).toBe(rem(px.icon))
      expect(light["header-button"], size).toBe(rem(px.button))
      expect(light["header-avatar"], size).toBe(rem(px.avatar))
      expect(dark["header-button"]).toBeUndefined()
    }
  })

  it("has the owner's pixels, Google's by default, and never outgrows the 56px header", () => {
    expect(HEADER_ICON_SIZE_PX).toEqual({
      current: { icon: 16, button: 36, avatar: 28 },
      google: { icon: 24, button: 40, avatar: 32 },
      large: { icon: 32, button: 48, avatar: 36 },
    })
    expect(DEFAULT_THEME_CONFIG.headerIconSize).toBe("google")
    for (const px of Object.values(HEADER_ICON_SIZE_PX)) {
      expect(px.button).toBeLessThan(56)
      expect(px.avatar).toBeLessThanOrEqual(px.button)
      expect(px.icon).toBeLessThan(px.button)
    }
  })

  it("caps a phone at Google's size in preset.css", () => {
    // The cap is CSS (a media query), so this reads the rule itself: a cap
    // that drifted from HEADER_ICON_SIZE_PHONE_CAP would let a large header
    // push the controls past a 375px screen, and nothing else would fail.
    const css = repoFile("Auth_UI/packages/ui/src/preset.css")
    const cap = HEADER_ICON_SIZE_PX[HEADER_ICON_SIZE_PHONE_CAP]
    expect(css).toContain(`--header-icon-drawn: min(var(--header-icon), ${rem(cap.icon)});`)
    expect(css).toContain(`--header-button-drawn: min(var(--header-button), ${rem(cap.button)});`)
    expect(css).toContain(`--header-avatar-drawn: min(var(--header-avatar), ${rem(cap.avatar)});`)
    // Lifted from the width where the sidebar stops being a drawer (use-mobile.ts: 768px).
    expect(css).toMatch(/@media \(width >= 48rem\) \{\s*:root \{\s*--header-icon-drawn: var\(--header-icon\);/)
  })
})

describe("the engine version", () => {
  // Browsers reuse a stylesheet computed for the same configuration and the
  // same THEME_ENGINE_VERSION. If this fingerprint moves, what the engine
  // computes moved: bump THEME_ENGINE_VERSION (theme-config.ts) and record
  // both new values here, or visitors keep the colours of the old engine.
  const RECORDED = {
    version: 5,
    fingerprint: "86a85adbdd2a7308",
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
        controlSize: { preset: "default" as const },
        tableRowHeight: { preset: "default" as const },
        headerIconSize: "google" as const,
      })),
      ...SIZE_STEPS.map((step) => ({
        ...DEFAULT_THEME_CONFIG,
        controlSize: { preset: step },
        tableRowHeight: { preset: step },
      })),
      {
        ...DEFAULT_THEME_CONFIG,
        controlSize: { preset: "custom" as const, px: 30 },
        tableRowHeight: { preset: "custom" as const, px: 61 },
      },
      ...HEADER_ICON_SIZES.map((headerIconSize) => ({ ...DEFAULT_THEME_CONFIG, headerIconSize })),
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

  it("names the same header icon sizes, in the same order", () => {
    expect(csharpList("HeaderIconSizes")).toEqual([...HEADER_ICON_SIZES])
  })

  function csharpNumbers(name: string): number[] {
    const match = new RegExp(`${name} =\\s*\\[([^\\]]*)\\]`).exec(source)
    expect(match, `${name} in ThemePresets.cs`).not.toBeNull()
    return [...match![1].matchAll(/\d+/g)].map((m) => Number(m[0]))
  }

  function csharpInt(name: string): number | undefined {
    const value = new RegExp(`const int ${name} = (\\d+);`).exec(source)?.[1]
    return value === undefined ? undefined : Number(value)
  }

  it("names the same size steps and heights, in the same order", () => {
    expect(csharpList("Sizes")).toEqual([...SIZE_STEPS])
    expect(csharpNumbers("ControlHeightsPx")).toEqual(SIZE_STEPS.map((step) => CONTROL_HEIGHT.steps[step]))
    expect(csharpNumbers("TableRowHeightsPx")).toEqual(SIZE_STEPS.map((step) => TABLE_ROW_HEIGHT.steps[step]))
    expect(csharpInt("MinControlHeightPx")).toBe(CONTROL_HEIGHT.min)
    expect(csharpInt("MaxControlHeightPx")).toBe(CONTROL_HEIGHT.max)
    expect(csharpInt("MinTableRowHeightPx")).toBe(TABLE_ROW_HEIGHT.min)
    expect(csharpInt("MaxTableRowHeightPx")).toBe(TABLE_ROW_HEIGHT.max)
  })

  it("keeps every pair of steps valid: the lowest row is no lower than the tallest control", () => {
    expect(Math.min(...Object.values(TABLE_ROW_HEIGHT.steps))).toBeGreaterThanOrEqual(
      Math.max(...Object.values(CONTROL_HEIGHT.steps))
    )
  })

  it("has the same defaults", () => {
    const constant = (name: string) =>
      new RegExp(`${name} = "([a-z-]+)"`).exec(source)?.[1]
    expect(constant("DefaultSize")).toBe(DEFAULT_THEME_CONFIG.controlSize.preset)
    expect(constant("DefaultSize")).toBe(DEFAULT_THEME_CONFIG.tableRowHeight.preset)
    expect(constant("DefaultBaseColor")).toBe(DEFAULT_THEME_CONFIG.base.preset)
    expect(constant("DefaultTheme")).toBe(DEFAULT_THEME_CONFIG.theme.preset)
    expect(constant("DefaultChartColor")).toBe(DEFAULT_THEME_CONFIG.chart.preset)
    expect(constant("DefaultRadius")).toBe(DEFAULT_THEME_CONFIG.radius)
    expect(constant("DefaultMenuAccent")).toBe(DEFAULT_THEME_CONFIG.menuAccent)
    expect(constant("DefaultHeaderIconSize")).toBe(DEFAULT_THEME_CONFIG.headerIconSize)
  })

  it("offers a base itself and the colourful themes, as shadcn does", () => {
    expect(themeNamesForBase("stone")).toEqual(["stone", ...ACCENT_COLOR_NAMES])
    expect(themeNamesForBase("custom")).toEqual(ACCENT_COLOR_NAMES)
  })
})
