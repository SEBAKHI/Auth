import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Hidden languages (UI batch P5, item 11, owner decision D8): a language an administrator hid is
 * not listed, never downloaded, and a page that would open in it opens in English. The modules
 * read the kept list when imported, so each case imports them fresh.
 *
 * Deliberate breaks, each turning a case here red: drop `visibleOrDefault` from
 * `getInitialLanguage` (the stored and hinted cases import them); drop it from `applyLanguage`
 * (the switch case); drop the second `visibleOrDefault` after the download (the late-hide case);
 * load the date locale apart from the strings (the Arabic dates case).
 */

/** Records which language modules were imported, the strings and the date-fns locale alike. */
function trackImports() {
  const imported: string[] = []
  for (const [specifier, name] of [
    ["./locales/fr", "strings:fr"],
    ["./locales/ar", "strings:ar"],
    ["date-fns/locale/fr", "dates:fr"],
    ["date-fns/locale/ar", "dates:ar"],
  ] as const) {
    vi.doMock(specifier, async (importOriginal) => {
      imported.push(name)
      return importOriginal()
    })
  }
  return imported
}

afterEach(() => {
  window.sessionStorage.clear()
  window.localStorage.clear()
  window.history.replaceState(null, "", "/")
  vi.doUnmock("./locales/fr")
  vi.doUnmock("./locales/ar")
  vi.doUnmock("date-fns/locale/fr")
  vi.doUnmock("date-fns/locale/ar")
  vi.resetModules()
})

describe("the hidden-languages store", () => {
  it("lists every language while nothing is hidden", async () => {
    const { getVisibleLanguages } = await import("./hidden-languages")

    expect(getVisibleLanguages().map((lang) => lang.code)).toEqual(["en", "ar", "tr", "fr", "zh", "ur", "fa"])
  })

  it("leaves hidden languages out, keeps the menus' order, and never hides English", async () => {
    const { getVisibleLanguages, isLanguageVisible, setHiddenLanguages, visibleOrDefault } =
      await import("./hidden-languages")

    setHiddenLanguages(["fa", "en", "xx", "fr", "fr"])

    expect(getVisibleLanguages().map((lang) => lang.code)).toEqual(["en", "ar", "tr", "zh", "ur"])
    expect(isLanguageVisible("en")).toBe(true)
    expect(visibleOrDefault("fr")).toBe("en")
    expect(visibleOrDefault("ar")).toBe("ar")
    expect(JSON.parse(window.localStorage.getItem("auth.languages.hidden")!)).toEqual(["fr", "fa"])
  })

  it("keeps its copy for the next load, and forgets it once nothing is hidden", async () => {
    let store = await import("./hidden-languages")
    store.setHiddenLanguages(["zh"])
    vi.resetModules()

    store = await import("./hidden-languages")
    expect(store.isLanguageVisible("zh")).toBe(false)

    store.setHiddenLanguages([])
    expect(window.localStorage.getItem("auth.languages.hidden")).toBeNull()
    vi.resetModules()
    store = await import("./hidden-languages")
    expect(store.isLanguageVisible("zh")).toBe(true)
  })

  it("reads an unreadable copy as nothing hidden", async () => {
    window.localStorage.setItem("auth.languages.hidden", "{not json")
    const { getVisibleLanguages } = await import("./hidden-languages")

    expect(getVisibleLanguages()).toHaveLength(7)
  })

  it("tells its listeners only when the list changed", async () => {
    const { setHiddenLanguages, subscribeHiddenLanguages } = await import("./hidden-languages")
    const listener = vi.fn()
    const unsubscribe = subscribeHiddenLanguages(listener)

    setHiddenLanguages(["tr"])
    setHiddenLanguages(["tr", "en"])
    unsubscribe()
    setHiddenLanguages([])

    expect(listener).toHaveBeenCalledTimes(1)
  })
})

describe("a hidden language is never loaded", () => {
  it("opens in English over a stored hidden language, and keeps the stored choice", async () => {
    window.localStorage.setItem("auth.languages.hidden", JSON.stringify(["fr"]))
    window.localStorage.setItem("auth.language", "fr")
    const imported = trackImports()

    const { initI18n } = await import("./index")
    const i18n = await initI18n()

    expect(i18n.language).toBe("en")
    expect(document.documentElement.lang).toBe("en")
    expect(imported).toEqual([])
    expect(window.localStorage.getItem("auth.language")).toBe("fr")
  })

  it("opens in English when an application's hint names a hidden language", async () => {
    window.localStorage.setItem("auth.languages.hidden", JSON.stringify(["ar"]))
    window.history.replaceState(null, "", "/login?ui_locales=ar")
    const imported = trackImports()
    const { captureEntryHints } = await import("./flow-hints")
    captureEntryHints({ pendingRequest: null, clientId: null, continuation: "never" })

    const { initI18n } = await import("./index")
    const i18n = await initI18n()

    expect(i18n.language).toBe("en")
    expect(document.documentElement.dir).toBe("ltr")
    expect(imported).toEqual([])
  })

  it("switches to English, without fetching, when asked for a hidden language", async () => {
    window.localStorage.setItem("auth.languages.hidden", JSON.stringify(["fr"]))
    const imported = trackImports()
    const { applyLanguage, default: i18n } = await import("./index")

    await applyLanguage("fr")

    expect(i18n.language).toBe("en")
    expect(imported).toEqual([])
  })

  it("does not switch to a language hidden while its bundle was on the way", async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => (release = resolve))
    vi.doMock("./locales/fr", async (importOriginal) => {
      await gate
      return importOriginal()
    })
    const { applyLanguage, default: i18n } = await import("./index")
    const { setHiddenLanguages } = await import("./hidden-languages")

    // A profile's French adopted at sign-in, then the branding hides French before it arrives.
    const pending = applyLanguage("fr")
    setHiddenLanguages(["fr"])
    release()
    await pending

    expect(i18n.language).toBe("en")
  })

  it("moves an open page to English the moment its language is hidden", async () => {
    window.localStorage.setItem("auth.language", "ar")
    const { initI18n } = await import("./index")
    const { setHiddenLanguages } = await import("./hidden-languages")
    const i18n = await initI18n()
    expect(i18n.language).toBe("ar")

    setHiddenLanguages(["ar"])

    await vi.waitFor(() => expect(i18n.language).toBe("en"))
    expect(window.localStorage.getItem("auth.language")).toBe("ar")
  })
})

describe("a language's dates load with its strings", () => {
  it("has the date locale in place once the language is active, and not before", async () => {
    const imported = trackImports()
    const { applyLanguage, dateLocaleFor } = await import("./index")
    expect(dateLocaleFor("ar")).toBeUndefined()

    await applyLanguage("ar")

    expect(imported.sort()).toEqual(["dates:ar", "strings:ar"])
    expect(dateLocaleFor("ar")?.code).toBe("ar")
    expect(dateLocaleFor("fr")).toBeUndefined()
  })

  it("has none for English and Urdu, whose dates stay English", async () => {
    const { applyLanguage, dateLocaleFor } = await import("./index")

    await applyLanguage("ur")

    expect(dateLocaleFor("ur")).toBeUndefined()
    expect(dateLocaleFor("en")).toBeUndefined()
  })
})
