/* eslint-disable react-refresh/only-export-components */
import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { useTheme, type ResolvedTheme } from "@/components/theme-provider"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { API_BASE_URL } from "@/lib/env"

export const BRANDING_QUERY_KEY = ["platform-branding"] as const

const DEFAULT_FAVICON = "/vite.svg"

interface BrandingValue {
  /** Platform display name (admin-configured, falls back to the app default). */
  name: string
  /**
   * Absolute logo URL for the active theme (falls back to the other theme's
   * logo), or null when no logo has been uploaded.
   */
  logoUrl: string | null
}

const BrandingContext = React.createContext<BrandingValue | undefined>(
  undefined
)

/** Uploads are served by the API host; resolve relative keys against it. */
function toAbsolute(url: string | null | undefined): string | null {
  if (!url) return null
  return url.startsWith("/") ? `${API_BASE_URL}${url}` : url
}

/** Picks the logo for the resolved theme, falling back to the other variant. */
export function pickLogo(
  light: string | null,
  dark: string | null,
  resolvedTheme: ResolvedTheme
): string | null {
  return resolvedTheme === "dark" ? (dark ?? light) : (light ?? dark)
}

/**
 * Fetches the public platform branding (anonymous endpoint) and applies it to
 * the browser tab (title + favicon). Screens read the name/logo via
 * `useBranding` — the sidebar header and the auth layout both do.
 */
export function BrandingProvider({ children }: { children: React.ReactNode }) {
  const { t } = useTranslation()
  const { resolvedTheme } = useTheme()

  const query = useQuery({
    queryKey: BRANDING_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Platform/branding")),
    staleTime: 5 * 60 * 1000,
  })

  const name = query.data?.platformName || t("common.appName")
  const logoUrl = pickLogo(
    toAbsolute(query.data?.logoUrl),
    toAbsolute(query.data?.logoUrlDark),
    resolvedTheme
  )
  // Dedicated square favicon when uploaded; wordmark logos are illegible at
  // tab-icon size, so admins can set a distinct mark. Falls back to the logo.
  const faviconUrl = toAbsolute(query.data?.faviconUrl) ?? logoUrl

  React.useEffect(() => {
    document.title = name
  }, [name])

  React.useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) return
    if (faviconUrl) {
      // Uploaded images are raster (webp); drop the svg type of the default icon.
      link.removeAttribute("type")
      link.href = faviconUrl
    } else {
      link.type = "image/svg+xml"
      link.href = DEFAULT_FAVICON
    }
  }, [faviconUrl])

  const value = React.useMemo<BrandingValue>(
    () => ({ name, logoUrl }),
    [name, logoUrl]
  )

  return (
    <BrandingContext.Provider value={value}>
      {children}
    </BrandingContext.Provider>
  )
}

export function useBranding(): BrandingValue {
  const context = React.useContext(BrandingContext)
  if (!context) {
    throw new Error("useBranding must be used within a BrandingProvider")
  }
  return context
}

/**
 * Square platform logo mark: the uploaded logo when set, otherwise the given
 * fallback (the default shield icon), in a preset-styled tile.
 */
export function BrandingLogo({
  fallback,
  className,
}: {
  fallback: React.ReactNode
  className?: string
}) {
  const { logoUrl, name } = useBranding()
  return logoUrl ? (
    <img
      src={logoUrl}
      alt={name}
      className={className ?? "size-8 rounded-lg object-cover"}
    />
  ) : (
    <>{fallback}</>
  )
}
