import { getReturnToClientId, getValidReturnTo } from "@authsystem/auth/return-to"

/**
 * Whose appearance the first frame is painted with: the application of the
 * pending authorize request in the address, validated by the same rule every
 * page of the flow applies, or null for the platform's. Never any other
 * application's: the cache is read by this one key (`applyCachedTheme`).
 *
 * Read once, at boot, before the router can rewrite the address; a page of the
 * flow then names its application itself (`useFlowBranding`).
 */
export function themeScopeFor(search: string): string | null {
  return getReturnToClientId(getValidReturnTo(search))
}
