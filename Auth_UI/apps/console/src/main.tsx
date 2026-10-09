import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./index.css"
import { initI18n } from "@authsystem/i18n"
import App from "./App.tsx"
import { installChunkLoadRecovery } from "@authsystem/ui/common/chunk-recovery"
import { applyCachedTheme } from "@authsystem/ui/theme/apply-theme"
import { ThemeProvider } from "@authsystem/ui/theme-provider.tsx"

// Registered before anything is imported on demand: a chunk removed by a deploy
// can fail during preload, which never reaches a route error boundary.
installChunkLoadRecovery()

// The platform's colours from the last visit, before anything is drawn: the
// current ones arrive with the branding, and until then the shipped preset
// would paint a returning visitor's first frames in the wrong colours.
applyCachedTheme()

// Locale bundles load on demand, so the active language must be in place before the
// first paint — otherwise the app renders a frame of raw translation keys.
//
// `finally`, not `then`: if the locale chunk fails to fetch the app must still boot.
// English is registered synchronously and i18next falls back to it per key, so the
// worst case is an English UI rather than a blank page.
void initI18n().finally(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <ThemeProvider>
        <App />
      </ThemeProvider>
    </StrictMode>
  )
})
