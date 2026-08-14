import type { Granularity } from "../use-dashboard-window"

/** What every dashboard tab needs: the shared window, and what the viewer may see. */
export interface DashboardScope {
  days: number
  granularity: Granularity
  timeZone: string
  permissions: {
    users: boolean
    apps: boolean
    roles: boolean
    audit: boolean
    allOrganizations: boolean
    /** Gates the API-key expiry finding, and the page it links to. */
    apiKeys: boolean
    /** Gates the webhook-key expiry finding, and the page it links to. */
    webhookKeys: boolean
  }
}
