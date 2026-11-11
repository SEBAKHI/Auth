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

type ExternalProviders = Awaited<ReturnType<typeof fetchExternalProviders>>

async function fetchExternalProviders() {
  const providers = await unwrap(api.GET("/api/v1/Auth/external-providers"))
  writeProvidersHint(providers.map((p) => p.code.toLowerCase()))
  return providers
}

/**
 * The providers query, shared by the hook below and by a button that must wait for the live
 * answer before it acts (the Google button: queryClient.fetchQuery with this, on click).
 */
export function externalProvidersQuery() {
  return {
    queryKey: ["external-providers"] as const,
    queryFn: fetchExternalProviders,
    staleTime: 5 * 60 * 1000,
  }
}

/**
 * What one answer of the API makes usable. The API already filters to providers it considers
 * usable; requiring a non-empty id as well keeps a button from rendering when neither the API
 * nor the build supplied one.
 */
export function resolveExternalProviders(data: ExternalProviders | undefined) {
  const byCode = new Map((data ?? []).map((p) => [p.code.toLowerCase(), p]))
  const google = byCode.get("google")
  const apple = byCode.get("apple")
  const googleClientId = google?.clientId || GOOGLE_CLIENT_ID
  const appleServicesId = apple?.clientId || APPLE_SERVICES_ID
  return {
    googleEnabled: Boolean(google) && googleClientId.length > 0,
    googleClientId,
    appleEnabled: Boolean(apple) && appleServicesId.length > 0,
    appleServicesId,
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
   * Whether Google is offered: the live answer once it is in, else what this browser heard
   * last time. The Google button shows on it, so it appears with the page; a click waits for
   * the live answer before it goes to Google, and the button goes when that answer says no.
   */
  googleExpected: boolean
  appleEnabled: boolean
  appleServicesId: string
} {
  const providersQuery = useQuery(externalProvidersQuery())
  const [hint] = React.useState(readProvidersHint)
  const resolved = resolveExternalProviders(providersQuery.data)

  return {
    ...resolved,
    googleExpected: providersQuery.data ? resolved.googleEnabled : hint.includes("google"),
  }
}
