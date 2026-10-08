import { useQuery } from "@tanstack/react-query"

import { API_BASE_URL } from "@authsystem/api/env"

export interface AppBranding {
  name: string
  logoUrl: string | null
}

async function fetchAppBranding(
  clientId: string,
  signal: AbortSignal
): Promise<AppBranding | null> {
  const res = await fetch(
    `${API_BASE_URL}/api/v1/applications/${encodeURIComponent(clientId)}/public-branding`,
    { signal }
  )
  // Thrown, not answered with null: a failure is not an answer worth keeping
  // for five minutes, and an errored entry is fetched again by the next screen.
  if (!res.ok) throw new Error(`public-branding ${res.status}`)
  const data = (await res.json()) as { name?: string; logoUrl?: string | null }
  return data?.name ? { name: data.name, logoUrl: data.logoUrl ?? null } : null
}

/**
 * Fetches the public branding (name + logo) of the application behind a
 * pending authorize request. Anonymous endpoint; any failure — unknown client,
 * network error — resolves to null so the page falls back to platform branding.
 *
 * Held in the shared query client, keyed by client, so the screens of one flow
 * share one answer: without it each step refetched, and the platform mark
 * flashed in the header at every step before the application's came back.
 */
export function useAppBranding(clientId: string | null): AppBranding | null {
  const query = useQuery({
    queryKey: ["public-branding", clientId],
    queryFn: ({ signal }) => fetchAppBranding(clientId!, signal),
    enabled: Boolean(clientId),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  // A response belongs only to the client that requested it: the key carries
  // the client and no placeholder is kept, so the previous application's
  // name/logo never flashes into the next authorization prompt.
  return clientId ? (query.data ?? null) : null
}
