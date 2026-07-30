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
  }
}
