import * as React from "react"

import { API_BASE_URL } from "@astoom/api/env"

export interface AppBranding {
  name: string
  logoUrl: string | null
}

/**
 * Fetches the public branding (name + logo) of the application behind a
 * pending authorize request. Anonymous endpoint; any failure — unknown client,
 * network error — resolves to null so the page falls back to platform branding.
 */
export function useAppBranding(clientId: string | null): AppBranding | null {
  const [branding, setBranding] = React.useState<AppBranding | null>(null)

  React.useEffect(() => {
    if (!clientId) {
      setBranding(null)
      return
    }

    let cancelled = false

    fetch(
      `${API_BASE_URL}/api/v1/applications/${encodeURIComponent(clientId)}/public-branding`,
    )
      .then((res) => (res.ok ? res.json() : null))
      .then((data: { name?: string; logoUrl?: string | null } | null) => {
        if (!cancelled && data?.name) {
          setBranding({ name: data.name, logoUrl: data.logoUrl ?? null })
        }
      })
      .catch(() => {
        // Fall back to platform branding silently.
      })

    return () => {
      cancelled = true
    }
  }, [clientId])

  return branding
}
