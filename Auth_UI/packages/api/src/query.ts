import { QueryClient, type QueryKey } from "@tanstack/react-query"

/**
 * Shared React Query client.
 *
 * `retry: 1` lets a transient 401 succeed on the second attempt after the auth
 * middleware has silently refreshed the access token.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
})

/**
 * Query keys whose data belongs to nobody in particular: each is served by an
 * anonymous endpoint and reads identically for every visitor.
 *
 * They are spared because clearing them buys no privacy and costs work. Their
 * providers sit ABOVE `AuthProvider` in both apps and so stay mounted across a
 * session boundary: removing an entry they are still observing makes them
 * refetch immediately. Measured in the browser by emptying this list and firing
 * the session-expired event — each boundary then re-requested the branding, the
 * provider list AND the branding logo image.
 *
 * What it does NOT prevent is a visual flash. `BrandingProvider` seeds itself
 * from a localStorage copy via `initialData`, so its data is never undefined
 * even when the entry is dropped; the same measurement showed zero frames of
 * changed markup either way. The cost this avoids is network churn on every
 * sign-out, expiry and sign-in — not a flicker.
 *
 * Membership is a claim about the ENDPOINT, not about convenience: add a key
 * only when an anonymous caller would receive the same bytes.
 */
const PUBLIC_QUERY_KEYS: readonly string[] = [
  "platform-branding",
  "external-providers",
  "privacy-policy-version",
  "password-policy",
]

function isPublicQuery(key: QueryKey): boolean {
  return typeof key[0] === "string" && PUBLIC_QUERY_KEYS.includes(key[0])
}

/**
 * Drops every cached query belonging to the outgoing user.
 *
 * The client is a module singleton mounted above the auth provider, so it
 * outlives every session on the tab — signing out tears down nothing. Left
 * alone, the next account reads the previous account's rows, and because
 * `["me"]` is unscoped and stays fresh for 30 seconds a quick switch never even
 * refetches.
 *
 * The stale avatar is the visible half. The damaging half is that the profile
 * form seeds its react-hook-form defaults from that entry once, at mount: a
 * save then writes one person's name, phone and preferences onto another
 * person's account.
 *
 * Removal, not invalidation: an invalidated entry is still SERVED while it
 * refetches, and that frame is precisely the one that must never render.
 * Cancellation comes first so a request already on the wire cannot resolve
 * after the removal and repopulate the cache under the incoming account.
 */
export async function resetUserScopedCache(
  client: QueryClient = queryClient
): Promise<void> {
  const userScoped = (key: QueryKey) => !isPublicQuery(key)

  await client.cancelQueries({ predicate: (q) => userScoped(q.queryKey) })
  client.removeQueries({ predicate: (q) => userScoped(q.queryKey) })
}
