import { getReturnToClientId, getValidReturnTo } from "@authsystem/auth/return-to"

/**
 * The pages that name an application by the `client_id` of their own address,
 * with no request to resume: the sign-out confirmation, the signed-out page and
 * the expired request. Each one passes that client id to `useFlowBranding`; a
 * test holds this list to the routes that render them.
 */
export const CLIENT_ID_PAGES: readonly string[] = ["/logout", "/signed-out", "/sign-in-expired"]

/** The router matches a path whatever its case, and with or without a trailing slash. */
function isClientIdPage(pathname: string): boolean {
  const path = pathname.toLowerCase().replace(/\/+$/, "")
  return CLIENT_ID_PAGES.includes(path)
}

/**
 * Whose appearance the first frame is painted with: the application the page
 * names, or null for the platform's. On a page of a pending authorize request,
 * the request's application, validated by the same rule every page of the flow
 * applies; on the pages that name one by its client id (`CLIENT_ID_PAGES`), that
 * one. Never any other application's: the cache is read by this one key
 * (`applyCachedTheme`). An application this browser has not seen switched on,
 * the server does not know or has switched off, has no cached sheet of its own,
 * and the platform's paints.
 *
 * Read once, at boot, before the router can rewrite the address; the page then
 * names its application itself (`useFlowBranding`).
 */
export function themeScopeFor({
  pathname,
  search,
}: {
  pathname: string
  search: string
}): string | null {
  if (isClientIdPage(pathname)) {
    return new URLSearchParams(search).get("client_id") || null
  }
  return getReturnToClientId(getValidReturnTo(search))
}
