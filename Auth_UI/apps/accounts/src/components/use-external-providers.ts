import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { APPLE_SERVICES_ID, GOOGLE_CLIENT_ID } from "@authsystem/api/env"
import { unwrap } from "@authsystem/api/helpers"

/**
 * Which external sign-in options are usable: the API must list the provider as
 * enabled AND the matching client id must be configured at build time. The
 * query is shared (same key) across every consumer, so it runs once per page.
 */
export function useExternalProviders(): {
  googleEnabled: boolean
  appleEnabled: boolean
} {
  const providersQuery = useQuery({
    queryKey: ["external-providers"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/external-providers")),
    staleTime: 5 * 60 * 1000,
  })

  const codes = (providersQuery.data ?? []).map((p) => p.code.toLowerCase())

  return {
    googleEnabled: GOOGLE_CLIENT_ID.length > 0 && codes.includes("google"),
    appleEnabled: APPLE_SERVICES_ID.length > 0 && codes.includes("apple"),
  }
}
