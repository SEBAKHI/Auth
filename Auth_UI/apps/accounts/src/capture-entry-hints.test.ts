import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The side-effect module main.tsx imports first: importing it reads the hints
 * off the address the tab arrived at and takes them off it, before anything
 * else (the router above all) reads that address.
 */
describe("capture-entry-hints", () => {
  afterEach(() => {
    window.sessionStorage.clear()
    window.history.replaceState(null, "", "/")
    vi.resetModules()
  })

  it("keeps the arrival's hints and leaves the address without them", async () => {
    window.history.replaceState(null, "", "/profile?ui_locales=fa&ui_theme=dark&tab=security")

    await import("./capture-entry-hints")
    const { readFlowHints } = await import("@authsystem/i18n/flow-hints")

    expect(readFlowHints()).toEqual({ language: "fa", theme: "dark" })
    expect(window.location.search).toBe("?tab=security")
  })
})
