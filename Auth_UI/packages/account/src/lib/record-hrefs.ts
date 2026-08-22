import { routePath } from "@authsystem/ui/route-path"

/**
 * Organizations are the one record type both host apps mount at the same path,
 * so the shared pages own this href themselves.
 *
 * The other drill-downs a shared page can offer - a member's user page, an
 * organization's application page - exist only in the console, so those arrive
 * as props from the host app rather than being assumed here.
 */
export const organizationHref = (id: string | undefined) =>
  id ? routePath`/organizations/${id}` : undefined
