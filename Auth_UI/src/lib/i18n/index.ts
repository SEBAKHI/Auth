import i18n from "i18next"
import { initReactI18next } from "react-i18next"

import { ar } from "./locales/ar"
import { en } from "./locales/en"

export const SUPPORTED_LANGUAGES = [
  { code: "en", label: "English", dir: "ltr" },
  { code: "ar", label: "العربية", dir: "rtl" },
] as const

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]["code"]

const LANGUAGE_STORAGE_KEY = "auth.language"

function isLanguageCode(value: string | null): value is LanguageCode {
  return value === "en" || value === "ar"
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

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
  },
  lng: getInitialLanguage(),
  fallbackLng: "en",
  interpolation: { escapeValue: false },
})

export function persistLanguage(code: LanguageCode): void {
  try {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, code)
  } catch {
    /* ignore */
  }
}

export function directionForLanguage(code: string): "ltr" | "rtl" {
  return code === "ar" ? "rtl" : "ltr"
}

export default i18n
