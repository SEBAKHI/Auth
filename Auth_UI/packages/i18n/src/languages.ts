/**
 * The languages the apps ship, in the order the language menus list them.
 *
 * Its own module so the flow hints can name it without importing the i18next
 * instance that `index.ts` initializes — the hints are read while that module is
 * still initializing. `Auth.Domain/Constants/Languages.cs` is the server's copy;
 * `flow-hints.test.ts` holds the two together.
 */
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

/** The language every page falls back to: always shipped, never hidden. */
export const DEFAULT_LANGUAGE: LanguageCode = "en"

export function isLanguageCode(value: string | null | undefined): value is LanguageCode {
  return SUPPORTED_LANGUAGES.some((lang) => lang.code === value)
}
