import i18n from "i18next"
import { initReactI18next } from "react-i18next"

import { ar } from "./locales/ar"
import { en } from "./locales/en"
import { fa } from "./locales/fa"
import { fr } from "./locales/fr"
import { tr } from "./locales/tr"
import { ur } from "./locales/ur"
import { zh } from "./locales/zh"

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

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
    tr: { translation: tr },
    fr: { translation: fr },
    zh: { translation: zh },
    ur: { translation: ur },
    fa: { translation: fa },
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
  return SUPPORTED_LANGUAGES.find((lang) => lang.code === code)?.dir ?? "ltr"
}

export default i18n
