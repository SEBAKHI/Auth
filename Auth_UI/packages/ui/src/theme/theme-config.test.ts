import { describe, expect, it } from "vitest"

import {
  CONTROL_HEIGHT,
  DEFAULT_THEME_CONFIG,
  TABLE_ROW_HEIGHT,
  sizePx,
  themeConfigKey,
  toThemeConfig,
  toThemeRequest,
} from "./theme-config"

describe("toThemeConfig", () => {
  it("is the shipped preset for an API that sends no appearance", () => {
    expect(toThemeConfig(undefined)).toEqual(DEFAULT_THEME_CONFIG)
    expect(toThemeConfig(null)).toEqual(DEFAULT_THEME_CONFIG)
  })

  it("keeps what it knows and defaults only what it does not", () => {
    // An API newer than this bundle may know a radius this bundle does not;
    // the rest of the appearance still applies.
    const config = toThemeConfig({
      base: { preset: "stone" },
      theme: { preset: "custom", light: "#112233", dark: "#445566" },
      chart: {},
      radius: "enormous",
      menuAccent: "bold",
    })

    expect(config).toEqual({
      base: { preset: "stone", light: null, dark: null },
      theme: { preset: "custom", light: "#112233", dark: "#445566" },
      chart: DEFAULT_THEME_CONFIG.chart,
      radius: "default",
      menuAccent: "bold",
      controlSize: { preset: "default" },
      tableRowHeight: { preset: "default" },
      headerIconSize: "google",
    })
  })

  it("reads the header icon size, and Google's for one it does not know or that is absent", () => {
    expect(toThemeConfig({ headerIconSize: "large" }).headerIconSize).toBe("large")
    expect(toThemeConfig({ headerIconSize: "current" }).headerIconSize).toBe("current")
    expect(toThemeConfig({ headerIconSize: "enormous" }).headerIconSize).toBe("google")
    expect(toThemeConfig({}).headerIconSize).toBe("google")
  })

  it("reads the two heights: a step, or custom pixels (int32 may arrive as a string)", () => {
    const config = toThemeConfig({
      controlSize: { preset: "compact", px: 99 },
      tableRowHeight: { preset: "custom", px: "61" },
    })

    expect(config.controlSize).toEqual({ preset: "compact" })
    expect(config.tableRowHeight).toEqual({ preset: "custom", px: 61 })
  })

  it.each([
    ["an API older than the heights", undefined],
    ["an unknown step", { preset: "spacious" }],
    ["custom without pixels", { preset: "custom" }],
    ["custom below the bounds", { preset: "custom", px: 27 }],
    ["custom above the bounds", { preset: "custom", px: 57 }],
    ["custom with a fraction", { preset: "custom", px: 33.5 }],
  ])("reads %s as the default height", (_, controlSize) => {
    expect(toThemeConfig({ controlSize }).controlSize).toEqual({ preset: "default" })
  })
})

describe("sizePx", () => {
  it("is each step's height, and a custom height as typed", () => {
    expect(sizePx({ preset: "compact" }, CONTROL_HEIGHT)).toBe(32)
    expect(sizePx({ preset: "default" }, CONTROL_HEIGHT)).toBe(36)
    expect(sizePx({ preset: "comfortable" }, TABLE_ROW_HEIGHT)).toBe(48)
    expect(sizePx({ preset: "custom", px: 50 }, TABLE_ROW_HEIGHT)).toBe(50)
    expect(sizePx({ preset: "custom", px: 80 }, TABLE_ROW_HEIGHT)).toBe(44)
  })
})

describe("toThemeRequest", () => {
  it("sends colours only with a custom choice", () => {
    const body = toThemeRequest({
      ...DEFAULT_THEME_CONFIG,
      base: { preset: "zinc", light: "#000000", dark: "#ffffff" },
      chart: { preset: "custom", light: "#16a34a", dark: "#22c55e" },
    })

    expect(body.base).toEqual({ preset: "zinc", light: null, dark: null })
    expect(body.chart).toEqual({ preset: "custom", light: "#16a34a", dark: "#22c55e" })
  })

  it("sends pixels only with a custom height", () => {
    const body = toThemeRequest({
      ...DEFAULT_THEME_CONFIG,
      controlSize: { preset: "comfortable", px: 30 },
      tableRowHeight: { preset: "custom", px: 52 },
    })

    expect(body.controlSize).toEqual({ preset: "comfortable", px: null })
    expect(body.tableRowHeight).toEqual({ preset: "custom", px: 52 })
  })
})

describe("themeConfigKey", () => {
  it("ignores leftover colours of a choice that is no longer custom", () => {
    // Otherwise switching away from custom and back to the saved preset would
    // still read as an unsaved change.
    expect(
      themeConfigKey({ ...DEFAULT_THEME_CONFIG, theme: { preset: "neutral", light: "#123456" } })
    ).toBe(themeConfigKey(DEFAULT_THEME_CONFIG))
    expect(
      themeConfigKey({ ...DEFAULT_THEME_CONFIG, controlSize: { preset: "default", px: 40 } })
    ).toBe(themeConfigKey(DEFAULT_THEME_CONFIG))
  })
})

describe("toThemeRequest — the header icon size", () => {
  it("sends the chosen size as its name", () => {
    expect(toThemeRequest({ ...DEFAULT_THEME_CONFIG, headerIconSize: "large" }).headerIconSize).toBe(
      "large"
    )
    expect(toThemeRequest(DEFAULT_THEME_CONFIG).headerIconSize).toBe("google")
  })
})
