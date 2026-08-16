import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { APPLE_SERVICES_ID, GOOGLE_CLIENT_ID } from "@authsystem/api/env"
import { unwrap } from "@authsystem/api/helpers"

/**
 * Which external sign-in options are usable, and the public client id each one
 * must be initialized with.
 *
 * The client id comes from the API, which serves the same value it validates the
 * returned token's audience against. That is the point: the client that MINTS
 * the token and the server that VERIFIES it read one source of truth, so a
 * provider configured in the system-settings console works without rebuilding
 * this app. The build-time `VITE_*` constants remain only as a fallback for an
 * API older than this build; once the API supplies a value it always wins.
 */
export function useExternalProviders(): {
  googleEnabled: boolean
  googleClientId: string
  appleEnabled: boolean
  appleServicesId: string
} {
  const providersQuery = useQuery({
    queryKey: ["external-providers"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/external-providers")),
    staleTime: 5 * 60 * 1000,
  })

  const byCode = new Map(
    (providersQuery.data ?? []).map((p) => [p.code.toLowerCase(), p])
  )

  const google = byCode.get("google")
  const apple = byCode.get("apple")

  const googleClientId = google?.clientId || GOOGLE_CLIENT_ID
  const appleServicesId = apple?.clientId || APPLE_SERVICES_ID

  return {
    // The API already filters to providers it considers usable; requiring a
    // non-empty id as well keeps the button from rendering when neither the API
    // nor the build supplied one.
    googleEnabled: Boolean(google) && googleClientId.length > 0,
    googleClientId,
    appleEnabled: Boolean(apple) && appleServicesId.length > 0,
    appleServicesId,
  }
}
