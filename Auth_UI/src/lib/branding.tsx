/* eslint-disable react-refresh/only-export-components */
import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { API_BASE_URL } from "@/lib/env"

export const BRANDING_QUERY_KEY = ["platform-branding"] as const

const DEFAULT_FAVICON = "/vite.svg"

interface BrandingValue {
  /** Platform display name (admin-configured, falls back to the app default). */
  name: string
  /** Absolute logo URL, or null when no logo has been uploaded. */
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

/**
 * Fetches the public platform branding (anonymous endpoint) and applies it to
 * the browser tab (title + favicon). Screens read the name/logo via
 * `useBranding` — the sidebar header and the auth layout both do.
 */
export function BrandingProvider({ children }: { children: React.ReactNode }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: BRANDING_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Platform/branding")),
    staleTime: 5 * 60 * 1000,
  })

  const name = query.data?.platformName || t("common.appName")
  const logoUrl = toAbsolute(query.data?.logoUrl)

  React.useEffect(() => {
    document.title = name
  }, [name])

  React.useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) return
    if (logoUrl) {
      // Uploaded logos are raster (webp); drop the svg type of the default icon.
      link.removeAttribute("type")
      link.href = logoUrl
    } else {
      link.type = "image/svg+xml"
      link.href = DEFAULT_FAVICON
    }
  }, [logoUrl])

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
