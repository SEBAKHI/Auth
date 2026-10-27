import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The application's language against the one stored on this browser: the hint
 * wins while its flow is in force, and a language the user then chooses ends it.
 * The module picks its language when imported, so each case imports it fresh.
 */
describe("initI18n with a flow hint", () => {
  afterEach(() => {
    window.sessionStorage.clear()
    window.localStorage.clear()
    window.history.replaceState(null, "", "/")
    vi.resetModules()
  })

  it("opens in the application's language over the stored one, and stores nothing", async () => {
    window.localStorage.setItem("auth.language", "ar")
    window.history.replaceState(null, "", "/login?ui_locales=fa")
    const { captureEntryHints } = await import("./flow-hints")
    captureEntryHints(null)

    const { initI18n } = await import("./index")
    const i18n = await initI18n()

    expect(i18n.language).toBe("fa")
    expect(document.documentElement.dir).toBe("rtl")
    expect(window.localStorage.getItem("auth.language")).toBe("ar")
  })

  it("ends the hint when the user stores a language of their own", async () => {
    window.history.replaceState(null, "", "/login?ui_locales=fa&ui_theme=dark")
    const { captureEntryHints, readFlowHints } = await import("./flow-hints")
    captureEntryHints(null)

    const { persistLanguage } = await import("./index")
    persistLanguage("tr")

    expect(readFlowHints()).toEqual({ theme: "dark" })
    expect(window.localStorage.getItem("auth.language")).toBe("tr")
  })

  it("falls back to the stored language when no hint is in force", async () => {
    window.localStorage.setItem("auth.language", "zh")

    const { initI18n } = await import("./index")
    const i18n = await initI18n()

    expect(i18n.language).toBe("zh")
  })
})
