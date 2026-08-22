import * as React from "react"

import { useTheme } from "@authsystem/ui/theme-provider"

import type { PreviewScheme } from "./email-preview-frame"

/** Where the chosen preview scheme is remembered, per browser. */
export const PREVIEW_SCHEME_STORAGE_KEY = "notifications.previewScheme"

function readStored(): PreviewScheme | null {
  try {
    const stored = localStorage.getItem(PREVIEW_SCHEME_STORAGE_KEY)
    return stored === "light" || stored === "dark" ? stored : null
  } catch {
    // Storage can be unavailable (private mode, blocked cookies). The preview
    // still works; it just starts from the console theme every time.
    return null
  }
}

/**
 * Which colour scheme an email preview simulates, remembered across pages and
 * sessions and seeded from the console's own theme the first time.
 *
 * The scheme is a property of the review, not of the reviewer's screen, so it
 * is chosen once and then held: a later theme change - including the "d"
 * shortcut - does not move a preview the author already set, and re-rendering
 * cannot reset it. Two people can still disagree on the very first open, before
 * either has chosen, and that is the price of not making a dark-mode console
 * open a light-mode preview; every open after that is the explicit value the
 * toggle shows on screen.
 *
 * Shared by the authoring preview and the delivery-log inspector, so an
 * operator investigating a complaint opens the scheme they last worked in.
 */
export function usePreviewScheme(): [
  PreviewScheme,
  (next: PreviewScheme) => void,
] {
  const { resolvedTheme } = useTheme()
  // Read once. `resolvedTheme` is only a seed for a browser that has never
  // chosen; binding to it would let a theme toggle rewrite the preview.
  const [scheme, setScheme] = React.useState<PreviewScheme>(
    () => readStored() ?? resolvedTheme
  )

  const choose = React.useCallback((next: PreviewScheme) => {
    setScheme(next)
    try {
      localStorage.setItem(PREVIEW_SCHEME_STORAGE_KEY, next)
    } catch {
      // The choice still holds for this session.
    }
  }, [])

  return [scheme, choose]
}
