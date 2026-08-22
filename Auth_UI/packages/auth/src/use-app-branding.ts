import * as React from "react"

import { API_BASE_URL } from "@authsystem/api/env"

export interface AppBranding {
  name: string
  logoUrl: string | null
}

interface BrandingResult {
  clientId: string
  branding: AppBranding
}

/**
 * Fetches the public branding (name + logo) of the application behind a
 * pending authorize request. Anonymous endpoint; any failure — unknown client,
 * network error — resolves to null so the page falls back to platform branding.
 */
export function useAppBranding(clientId: string | null): AppBranding | null {
  const [result, setResult] = React.useState<BrandingResult | null>(null)

  React.useEffect(() => {
    if (!clientId) return

    let cancelled = false

    fetch(
      `${API_BASE_URL}/api/v1/applications/${encodeURIComponent(clientId)}/public-branding`,
    )
      .then((res) => (res.ok ? res.json() : null))
      .then((data: { name?: string; logoUrl?: string | null } | null) => {
        if (!cancelled && data?.name) {
          setResult({
            clientId,
            branding: { name: data.name, logoUrl: data.logoUrl ?? null },
          })
        }
      })
      .catch(() => {
        // Fall back to platform branding silently.
      })

    return () => {
      cancelled = true
    }
  }, [clientId])

  // A response belongs only to the client that requested it. Returning null
  // while a different client is loading prevents the previous application's
  // name/logo flashing into the next authorization prompt.
  return clientId && result?.clientId === clientId ? result.branding : null
}
