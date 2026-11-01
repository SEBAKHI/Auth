// FIRST, before any module that reads the address: the application's hints are
// taken off it here, and the router snapshots it when ./App.tsx is evaluated.
import "./capture-entry-hints"

import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./index.css"
import { initI18n } from "@authsystem/i18n"
import App from "./App.tsx"
import { installChunkLoadRecovery } from "@authsystem/ui/common/chunk-recovery"
import { applyCachedTheme } from "@authsystem/ui/theme/apply-theme"
import { applyCachedFonts } from "@authsystem/ui/theme/platform-fonts"
import { ThemeProvider } from "@authsystem/ui/theme-provider.tsx"
import { themeScopeFor } from "./theme-scope"

// Registered before anything is imported on demand: a chunk removed by a deploy
// can fail during preload, which never reaches a route error boundary.
installChunkLoadRecovery()

// The colours from the last visit, before anything is drawn: the current ones
// arrive with the branding, and until then the shipped preset would paint a
// returning visitor's first frames in the wrong colours. On a page that names
// an application, that application's own appearance when this browser has seen
// it switched on; otherwise, and on every other page, the platform's.
const themeScope = themeScopeFor(window.location)
applyCachedTheme(themeScope)

// The platform's fonts from the last visit, fetched beside the locale bundle: the first frame
// waits for them briefly (CACHED_FONTS_BUDGET_MS), so a returning visitor does not see the
// shipped font swap to the platform's. It never fails.
const cachedFonts = applyCachedFonts()

// Locale bundles load on demand, so the active language must be in place before the
// first paint — otherwise the app renders a frame of raw translation keys.
//
// Settled, not fulfilled: if the locale chunk fails to fetch the app must still boot.
// English is registered synchronously and i18next falls back to it per key, so the
// worst case is an English UI rather than a blank page.
void Promise.allSettled([initI18n(), cachedFonts]).then(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <ThemeProvider>
        <App applicationScope={themeScope} />
      </ThemeProvider>
    </StrictMode>
  )
})
