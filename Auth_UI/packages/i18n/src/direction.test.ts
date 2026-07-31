import { describe, expect, it, vi } from "vitest"

/**
 * The app has two ways to ask "which direction are we in", and they used to
 * disagree on exactly one path: a cold load with an RTL language already stored.
 *
 * `init()` resolves the language while only English is registered — the other six
 * bundles are fetched on demand — so i18next settles `resolvedLanguage` on `en`
 * even though `language` is `ar`, and `addResourceBundle` never recomputes it.
 * `DirectionProvider` reads `directionForLanguage(i18n.language)` and wrote `rtl`
 * onto the document, while `i18n.dir()` (which reads `resolvedLanguage`) answered
 * `ltr`. Consumers of the latter drew RTL layouts with LTR geometry: the data
 * table's column resize ran its drag maths backwards, and the row-detail sheet
 * opened from the wrong edge.
 *
 * Nothing in the type system couples the two, so this test does.
 */
describe("initI18n", () => {
  it("leaves i18next's own direction agreeing with the document's on a cold RTL load", async () => {
    // Neither Node 26 nor this jsdom setup exposes a usable `localStorage`, and
    // the module reads the stored language at import time — so the store has to
    // be in place, and stubbed, before the import below.
    const store = new Map([["auth.language", "ar"]])
    vi.stubGlobal("localStorage", {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
      removeItem: (key: string) => void store.delete(key),
    })

    const { initI18n, directionForLanguage } = await import("./index")
    const i18n = await initI18n()

    expect(i18n.language).toBe("ar")
    expect(directionForLanguage(i18n.language)).toBe("rtl")
    // The one place the banned call is the subject rather than the mistake.
    // eslint-disable-next-line no-restricted-syntax
    expect(i18n.dir()).toBe("rtl")
    expect(i18n.resolvedLanguage).toBe("ar")

    vi.unstubAllGlobals()
  })
})
