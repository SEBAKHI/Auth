import { useQuery } from "@tanstack/react-query"

import { API_BASE_URL } from "@authsystem/api/env"
import { toThemeConfig, type ThemeConfig } from "@authsystem/ui/theme/theme-config"

export interface AppBranding {
  name: string
  logoUrl: string | null
  /** The dark-mode logo; null when the application has one logo for both modes. */
  logoUrlDark: string | null
  /**
   * Where the application is hosted, for a way back when nothing else leads
   * there (OI-145); null unless it is safe to render as a link.
   */
  homeUrl: string | null
  /**
   * The application's own appearance, in place of the platform's on the pages
   * of its flow; null while it shows the platform's (switched off, or none).
   */
  theme: ThemeConfig | null
}

/**
 * The application's home address when it is safe to render as a link: an
 * absolute `https:` URL, or `http:` on the loopback host of local development.
 * The server already withholds anything else; the page does not rely on that.
 */
export function safeHomeUrl(raw: string | null | undefined): string | null {
  if (!raw) return null
  try {
    const url = new URL(raw)
    if (url.protocol === "https:") return url.toString()
    const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname)
    return url.protocol === "http:" && loopback ? url.toString() : null
  } catch {
    return null
  }
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
  const data = (await res.json()) as {
    name?: string
    logoUrl?: string | null
    logoUrlDark?: string | null
    homeUrl?: string | null
    theme?: Parameters<typeof toThemeConfig>[0]
  }
  return data?.name
    ? {
        name: data.name,
        logoUrl: data.logoUrl ?? null,
        logoUrlDark: data.logoUrlDark ?? null,
        homeUrl: safeHomeUrl(data.homeUrl),
        // The whole appearance or none: the server sends it only while it is
        // switched on, and it replaces the platform's, never merged with it.
        theme: data.theme ? toThemeConfig(data.theme) : null,
      }
    : null
}

/**
 * Fetches the public branding (name + logos) of the application behind a
 * pending authorize request. Anonymous endpoint; any failure — unknown client,
 * network error — resolves to null so the page falls back to platform branding.
 *
 * Held in the shared query client, keyed by client, so the screens of one flow
 * share one answer: without it each step refetched, and the platform mark
 * flashed in the header at every step before the application's came back.
 */
export function useAppBranding(clientId: string | null): AppBranding | null {
  const query = useAppBrandingQuery(clientId)

  // A response belongs only to the client that requested it: the key carries
  // the client and no placeholder is kept, so the previous application's
  // name/logo never flashes into the next authorization prompt.
  return clientId ? (query.data ?? null) : null
}

function useAppBrandingQuery(clientId: string | null) {
  return useQuery({
    queryKey: ["public-branding", clientId],
    queryFn: ({ signal }) => fetchAppBranding(clientId!, signal),
    enabled: Boolean(clientId),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

/**
 * The application's own appearance, from the same answer as `useAppBranding`:
 * null when the platform's shows (switched off, or none saved), undefined while
 * it is not known. A failed request leaves it unknown: the page keeps what it
 * shows, and the application's cached copy is not forgotten for a failure,
 * which says nothing about its appearance.
 */
export function useAppTheme(clientId: string | null): ThemeConfig | null | undefined {
  const query = useAppBrandingQuery(clientId)
  if (!clientId) return null
  if (query.data === undefined) return undefined
  return query.data?.theme ?? null
}
