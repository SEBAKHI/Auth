import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import * as React from "react"

import { initI18n } from "@authsystem/i18n"
import { setHiddenLanguages } from "@authsystem/i18n/hidden-languages"
import { writeCachedBranding } from "@authsystem/ui/branding-cache"
import { applyCachedTheme } from "@authsystem/ui/theme/apply-theme"
import { applyCachedFonts, applyPlatformFonts, fontUrlsFrom } from "@authsystem/ui/theme/platform-fonts"
import { toThemeConfig } from "@authsystem/ui/theme/theme-config"
import { applyPlan } from "@authsystem/ui/theme/theme-plan"

import { fetchPlatformBranding } from "./embed-api"
import { EmbedProviders } from "./embed-root"
import { readParentOrigin } from "./protocol"

/** The platform's appearance, fonts and hidden languages, as BrandingProvider applies them. */
async function applyPlatformBranding(): Promise<void> {
  try {
    const branding = await fetchPlatformBranding()
    writeCachedBranding(branding)
    setHiddenLanguages(branding.hiddenLanguages ?? [])
    void applyPlatformFonts(fontUrlsFrom(branding.fonts))
    applyPlan({ kind: "apply", config: toThemeConfig(branding.theme ?? null), scope: null })
  } catch (error) {
    // The cached appearance (or the shipped preset) stays, which is cosmetic.
    console.error("[account-bar] branding failed", error)
  }
}

/**
 * Boots a frame: nothing at all unless a linked application framed it (`readParentOrigin`),
 * then the cached look before the first paint, the language bundle, and the frame.
 */
export async function bootEmbed(render: () => React.ReactNode): Promise<void> {
  const parentOrigin = readParentOrigin()
  if (!parentOrigin) return

  applyCachedTheme(null)
  await Promise.allSettled([initI18n(), applyCachedFonts()])
  void applyPlatformBranding()

  const queryClient = new QueryClient({
    defaultOptions: {
      // One retry: a refusal at the edge (429) is not worth hammering, and a failed read
      // draws nothing rather than a wrong Sign in.
      queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 },
    },
  })

  const { createRoot } = await import("react-dom/client")
  createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
      <QueryClientProvider client={queryClient}>
        <EmbedProviders parentOrigin={parentOrigin}>{render()}</EmbedProviders>
      </QueryClientProvider>
    </React.StrictMode>
  )
}
