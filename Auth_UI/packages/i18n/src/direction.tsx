/* eslint-disable react-refresh/only-export-components */
import * as React from "react"
import { Direction } from "radix-ui"
import { useTranslation } from "react-i18next"

import {
  applyLanguage,
  directionForLanguage,
  persistLanguage,
  type LanguageCode,
} from "./index"

type Dir = "ltr" | "rtl"

interface LanguageContextValue {
  language: string
  dir: Dir
  setLanguage: (code: LanguageCode) => void
}

const LanguageContext = React.createContext<LanguageContextValue | undefined>(
  undefined
)

/**
 * Synchronizes the active i18n language with the document direction and wraps
 * the tree in Radix's DirectionProvider so every primitive renders RTL/LTR
 * correctly. Authoring uses logical CSS only, so no per-component overrides.
 */
export function DirectionProvider({ children }: { children: React.ReactNode }) {
  const { i18n } = useTranslation()
  const language = i18n.language
  const dir = directionForLanguage(language)

  React.useEffect(() => {
    document.documentElement.lang = language
    document.documentElement.dir = dir
  }, [language, dir])

  const setLanguage = React.useCallback((code: LanguageCode) => {
    persistLanguage(code)
    // Locale bundles are loaded on demand, so the switch has to wait for the
    // fetch; `applyLanguage` owns that ordering.
    void applyLanguage(code)
  }, [])

  const value = React.useMemo<LanguageContextValue>(
    () => ({ language, dir, setLanguage }),
    [language, dir, setLanguage]
  )

  return (
    <LanguageContext.Provider value={value}>
      <Direction.Provider dir={dir}>{children}</Direction.Provider>
    </LanguageContext.Provider>
  )
}

export function useLanguage(): LanguageContextValue {
  const context = React.useContext(LanguageContext)
  if (!context) {
    throw new Error("useLanguage must be used within a DirectionProvider")
  }
  return context
}
