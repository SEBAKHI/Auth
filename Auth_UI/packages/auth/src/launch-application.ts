import { safeHomeUrl } from "./use-app-branding"

/**
 * The address IAM may send the browser to after an accepted invitation (OI-192),
 * or null: an absolute `https:` URL, or `http:` on the loopback host of local
 * development, nothing else.
 *
 * The server builds the address from the application's registration alone, and
 * nothing in a request can change it. The page checks it again anyway, with the
 * same rule as the application's way home, so a value that is not a web address
 * (`javascript:`, a relative path) never reaches the location bar.
 */
export function launchTarget(raw: string | null | undefined): string | null {
  return safeHomeUrl(raw)
}

/**
 * Sends the browser to the application when the address passes
 * {@link launchTarget}, and says whether it did.
 *
 * A top-level navigation, not a router transition: the application is another
 * origin, and its sign-in start needs the IdP session cookie, which rides along
 * only with a navigation the browser makes itself.
 */
export function launchApplication(raw: string | null | undefined): boolean {
  const target = launchTarget(raw)
  if (!target) return false
  window.location.assign(target)
  return true
}
