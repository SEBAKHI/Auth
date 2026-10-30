import * as React from "react"
import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { APPLE_SERVICES_ID, GOOGLE_CLIENT_ID } from "@authsystem/api/env"
import { unwrap } from "@authsystem/api/helpers"

/**
 * The provider codes of this browser's last answer, kept to start loading a provider's
 * button before the live answer is in. Public and per deployment, like the cached
 * branding: nothing of the visitor's.
 */
const PROVIDERS_HINT_KEY = "auth.ui.externalProviders"

function readProvidersHint(): string[] {
  try {
    const raw = window.localStorage.getItem(PROVIDERS_HINT_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((code): code is string => typeof code === "string") : []
  } catch {
    // Storage is absent in privacy-restricted contexts and in jsdom.
    return []
  }
}

function writeProvidersHint(codes: string[]): void {
  try {
    window.localStorage.setItem(PROVIDERS_HINT_KEY, JSON.stringify(codes))
  } catch {
    // Best effort: without it the next visit waits for the live answer, as before.
  }
}

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
  /**
   * Whether to start loading Google's button now: the live answer once it is in,
   * else what this browser heard last time. It never decides that the button
   * shows, which takes `googleEnabled`; it only lets Google's script and the
   * server's nonce load while the live answer is on its way, instead of after it.
   */
  googleExpected: boolean
  appleEnabled: boolean
  appleServicesId: string
} {
  const providersQuery = useQuery({
    queryKey: ["external-providers"],
    queryFn: async () => {
      const providers = await unwrap(api.GET("/api/v1/Auth/external-providers"))
      writeProvidersHint(providers.map((p) => p.code.toLowerCase()))
      return providers
    },
    staleTime: 5 * 60 * 1000,
  })
  const [hint] = React.useState(readProvidersHint)

  const byCode = new Map(
    (providersQuery.data ?? []).map((p) => [p.code.toLowerCase(), p])
  )

  const google = byCode.get("google")
  const apple = byCode.get("apple")

  const googleClientId = google?.clientId || GOOGLE_CLIENT_ID
  const appleServicesId = apple?.clientId || APPLE_SERVICES_ID

  // The API already filters to providers it considers usable; requiring a
  // non-empty id as well keeps the button from rendering when neither the API
  // nor the build supplied one.
  const googleEnabled = Boolean(google) && googleClientId.length > 0

  return {
    googleEnabled,
    googleClientId,
    googleExpected: providersQuery.data ? googleEnabled : hint.includes("google"),
    appleEnabled: Boolean(apple) && appleServicesId.length > 0,
    appleServicesId,
  }
}
