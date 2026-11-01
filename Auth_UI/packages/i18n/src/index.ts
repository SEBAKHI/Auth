import type { Locale } from "date-fns"
import i18n from "i18next"
import { initReactI18next } from "react-i18next"

import { dropFlowHint, readFlowHints } from "./flow-hints"
import { isLanguageVisible, subscribeHiddenLanguages, visibleOrDefault } from "./hidden-languages"
import { DEFAULT_LANGUAGE, isLanguageCode, SUPPORTED_LANGUAGES, type LanguageCode } from "./languages"
import { en } from "./locales/en"

export { SUPPORTED_LANGUAGES, type LanguageCode }

const LANGUAGE_STORAGE_KEY = "auth.language"

/** What a language needs on the page: its strings, and how dates are written in it. */
interface LanguageResources {
  translation: unknown
  /** The date-fns locale; none for Urdu, which date-fns does not ship (dates stay English). */
  dateLocale?: Locale
}

/**
 * Dynamic imports per language, so only the active one ships in the entry chunk.
 *
 * The seven locale files are ~374 KB of source between them; loading all of them
 * eagerly meant every visitor downloaded six languages they will never read, in the
 * same chunk as the login screen. Each entry is a separate `import()` rather than a
 * template literal so the bundler can name and split the chunks statically.
 *
 * A language's date-fns locale loads beside its strings, in the same call, for the same
 * reason: imported statically by the date formatter, all five shipped in every page. Loaded
 * together, a language is never in place with its dates still English, so the formatter reads
 * the locale synchronously (`dateLocaleFor`). A hidden language's two never load at all.
 *
 * English is imported normally: it is the fallback, so it is needed on every render
 * regardless of the active language.
 */
const LOADERS: Record<Exclude<LanguageCode, "en">, () => Promise<LanguageResources>> = {
  ar: () =>
    Promise.all([import("./locales/ar"), import("date-fns/locale/ar")]).then(([strings, dates]) => ({
      translation: strings.ar,
      dateLocale: dates.ar,
    })),
  tr: () =>
    Promise.all([import("./locales/tr"), import("date-fns/locale/tr")]).then(([strings, dates]) => ({
      translation: strings.tr,
      dateLocale: dates.tr,
    })),
  fr: () =>
    Promise.all([import("./locales/fr"), import("date-fns/locale/fr")]).then(([strings, dates]) => ({
      translation: strings.fr,
      dateLocale: dates.fr,
    })),
  zh: () =>
    Promise.all([import("./locales/zh"), import("date-fns/locale/zh-CN")]).then(([strings, dates]) => ({
      translation: strings.zh,
      dateLocale: dates.zhCN,
    })),
  ur: () => import("./locales/ur").then((strings) => ({ translation: strings.ur })),
  fa: () =>
    Promise.all([import("./locales/fa"), import("date-fns/locale/fa-IR")]).then(([strings, dates]) => ({
      translation: strings.fa,
      dateLocale: dates.faIR,
    })),
}

const DATE_LOCALES = new Map<string, Locale>()

/**
 * The date-fns locale of a language whose bundle has loaded; undefined for English, for Urdu,
 * and for a language not loaded. Synchronous, because `loadLanguage` registers it before the
 * language can become active.
 */
export function dateLocaleFor(code: string): Locale | undefined {
  return DATE_LOCALES.get(code)
}

/**
 * The language a page opens in: the application's, while a flow it started is
 * in force (`flow-hints.ts`); then the one stored on this browser; then English.
 * A hidden language (`hidden-languages.ts`) opens in English instead, whichever of the
 * two named it: the stored choice is kept, for the day the language is shown again.
 */
function getInitialLanguage(): LanguageCode {
  const hinted = readFlowHints().language
  if (hinted) return visibleOrDefault(hinted)

  try {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY)
    if (isLanguageCode(stored)) return visibleOrDefault(stored)
  } catch {
    /* ignore */
  }
  return "en"
}

/**
 * Fetch and register a language's resources. Safe to call repeatedly: i18next
 * already holds the bundle after the first load, so this returns immediately.
 * A hidden language is never fetched.
 */
export async function loadLanguage(code: LanguageCode): Promise<void> {
  if (code === "en" || !isLanguageVisible(code) || i18n.hasResourceBundle(code, "translation")) return
  const resources = await LOADERS[code]()
  if (resources.dateLocale) DATE_LOCALES.set(code, resources.dateLocale)
  i18n.addResourceBundle(code, "translation", resources.translation, true, true)
}

/**
 * Switch the active language, fetching its bundle first; a hidden one switches to English.
 *
 * Always go through this rather than `i18n.changeLanguage` directly: switching
 * before the bundle has loaded renders the raw keys until it arrives.
 */
export async function applyLanguage(code: LanguageCode): Promise<void> {
  const shown = visibleOrDefault(code)
  await loadLanguage(shown)
  // Asked again: the branding may have hidden it while its bundle was on the way (a profile's
  // language adopted at sign-in, before the first branding answer), and the listener below
  // only sees the language already active.
  await i18n.changeLanguage(visibleOrDefault(shown))
}

// The page's language hidden while it is open (the branding's answer brought a new list):
// English now. The stored choice stays, as at load.
subscribeHiddenLanguages(() => {
  if (!isLanguageVisible(i18n.language)) void applyLanguage(DEFAULT_LANGUAGE)
})

/**
 * English is registered synchronously on import, so `t()` resolves the moment this
 * module is loaded. That keeps the long-standing `import "@authsystem/i18n"` contract
 * — several tests rely on it — while the other six languages load on demand.
 *
 * `lng` starts at the stored language even though its bundle has not arrived yet:
 * i18next falls back to English per key, so the app is readable either way, and
 * `initI18n` closes the gap before the first paint.
 */
void i18n.use(initReactI18next).init({
  resources: { en: { translation: en } },
  lng: getInitialLanguage(),
  fallbackLng: "en",
  interpolation: { escapeValue: false },
})

/**
 * Load the stored language's bundle. Entry points must await this before rendering:
 * react-i18next would otherwise paint one frame of English (or of raw keys) while
 * the active bundle was still in flight.
 *
 * The trailing `changeLanguage` is not redundant. `init` above resolves the language
 * while only English is registered, so i18next settles `resolvedLanguage` on `en`
 * even though `language` is `ar` — and `addResourceBundle` never recomputes it.
 * Everything i18next derives from `resolvedLanguage` then reports English for an
 * Arabic session: `i18n.dir()` answers `ltr` against an RTL document, and
 * `getFixedT()` falls back to the wrong locale. `changeLanguage` is the only path
 * that recomputes it, and by here the bundle it needs has arrived.
 *
 * The document's own direction is stamped here too, not only in `DirectionProvider`.
 * That provider sets it from an effect, which runs after the first commit, and
 * `index.html` ships `<html lang="en">` with no `dir` — so an Arabic session
 * painted its first frame left-to-right and then flipped. Entry points await this
 * before rendering, so writing it now makes the first frame already correct; the
 * provider's effect still owns in-session language switches.
 */
export async function initI18n(): Promise<typeof i18n> {
  const code = getInitialLanguage()
  await loadLanguage(code)
  await i18n.changeLanguage(code)
  document.documentElement.lang = code
  document.documentElement.dir = directionForLanguage(code)
  return i18n
}

/**
 * Stores a language the user chose (or the profile's, adopted at sign-in). It
 * also ends an application's language hint: a choice made here outranks it.
 */
export function persistLanguage(code: LanguageCode): void {
  dropFlowHint("language")
  try {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, code)
  } catch {
    /* ignore */
  }
}

export function directionForLanguage(code: string): "ltr" | "rtl" {
  return SUPPORTED_LANGUAGES.find((lang) => lang.code === code)?.dir ?? "ltr"
}

export default i18n
