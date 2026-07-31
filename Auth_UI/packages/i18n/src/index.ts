import i18n from "i18next"
import { initReactI18next } from "react-i18next"

import { en } from "./locales/en"

export const SUPPORTED_LANGUAGES = [
  { code: "en", label: "English", dir: "ltr" },
  { code: "ar", label: "العربية", dir: "rtl" },
  { code: "tr", label: "Türkçe", dir: "ltr" },
  { code: "fr", label: "Français", dir: "ltr" },
  { code: "zh", label: "中文", dir: "ltr" },
  { code: "ur", label: "اردو", dir: "rtl" },
  { code: "fa", label: "فارسی", dir: "rtl" },
] as const

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]["code"]

const LANGUAGE_STORAGE_KEY = "auth.language"

/**
 * Dynamic imports per language, so only the active one ships in the entry chunk.
 *
 * The seven locale files are ~374 KB of source between them; loading all of them
 * eagerly meant every visitor downloaded six languages they will never read, in the
 * same chunk as the login screen. Each entry is a separate `import()` rather than a
 * template literal so the bundler can name and split the chunks statically.
 *
 * English is imported normally: it is the fallback, so it is needed on every render
 * regardless of the active language.
 */
const LOADERS: Record<
  Exclude<LanguageCode, "en">,
  () => Promise<{ default?: unknown } & Record<string, unknown>>
> = {
  ar: () => import("./locales/ar"),
  tr: () => import("./locales/tr"),
  fr: () => import("./locales/fr"),
  zh: () => import("./locales/zh"),
  ur: () => import("./locales/ur"),
  fa: () => import("./locales/fa"),
}

function isLanguageCode(value: string | null): value is LanguageCode {
  return SUPPORTED_LANGUAGES.some((lang) => lang.code === value)
}

function getInitialLanguage(): LanguageCode {
  try {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY)
    if (isLanguageCode(stored)) return stored
  } catch {
    /* ignore */
  }
  return "en"
}

/**
 * Fetch and register a language's resources. Safe to call repeatedly: i18next
 * already holds the bundle after the first load, so this returns immediately.
 */
export async function loadLanguage(code: LanguageCode): Promise<void> {
  if (code === "en" || i18n.hasResourceBundle(code, "translation")) return
  const module = await LOADERS[code]()
  i18n.addResourceBundle(code, "translation", module[code], true, true)
}

/**
 * Switch the active language, fetching its bundle first.
 *
 * Always go through this rather than `i18n.changeLanguage` directly: switching
 * before the bundle has loaded renders the raw keys until it arrives.
 */
export async function applyLanguage(code: LanguageCode): Promise<void> {
  await loadLanguage(code)
  await i18n.changeLanguage(code)
}

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

export function persistLanguage(code: LanguageCode): void {
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
