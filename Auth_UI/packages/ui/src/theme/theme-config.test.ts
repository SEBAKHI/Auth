import { describe, expect, it } from "vitest"

import {
  DEFAULT_THEME_CONFIG,
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
    })
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
})

describe("themeConfigKey", () => {
  it("ignores leftover colours of a choice that is no longer custom", () => {
    // Otherwise switching away from custom and back to the saved preset would
    // still read as an unsaved change.
    expect(
      themeConfigKey({ ...DEFAULT_THEME_CONFIG, theme: { preset: "neutral", light: "#123456" } })
    ).toBe(themeConfigKey(DEFAULT_THEME_CONFIG))
  })
})
