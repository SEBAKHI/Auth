import { useQuery } from "@tanstack/react-query"

import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"

import { dashboardKeys } from "./use-dashboard-window"

/**
 * One hook per server aggregate.
 *
 * Each is called by the tab that needs it, so a tab fetches only its own data —
 * the previous single page fired ten requests on load regardless of what the
 * reader was looking at.
 *
 * `placeholderData` keeps the previous window's response on screen while a new one
 * loads, so changing the period dims the cards instead of collapsing them into
 * skeletons and jumping the layout.
 */
const keepPrevious = <T,>(previous: T | undefined) => previous

export function useUserStats(days: number, timeZone: string, enabled: boolean) {
  return useQuery({
    queryKey: dashboardKeys.userStats(days, timeZone),
    enabled,
    placeholderData: keepPrevious,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/user-stats", {
          params: { query: { days, timeZone } },
        })
      ),
  })
}

export function useAuthStats(days: number, timeZone: string, enabled: boolean) {
  return useQuery({
    queryKey: dashboardKeys.authStats(days, timeZone),
    enabled,
    placeholderData: keepPrevious,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/auth-stats", {
          params: { query: { days, timeZone } },
        })
      ),
  })
}

export function useAuditStats(days: number, timeZone: string, enabled: boolean) {
  return useQuery({
    queryKey: dashboardKeys.auditStats(days, timeZone),
    enabled,
    placeholderData: keepPrevious,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/audit-stats", {
          params: { query: { days, timeZone } },
        })
      ),
  })
}

export function useSessionStats(days: number, enabled: boolean) {
  return useQuery({
    queryKey: dashboardKeys.sessionStats(days),
    enabled,
    placeholderData: keepPrevious,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/session-stats", {
          params: { query: { days } },
        })
      ),
  })
}

export function useAppActivity(days: number, enabled: boolean) {
  return useQuery({
    queryKey: dashboardKeys.appActivity(days),
    enabled,
    placeholderData: keepPrevious,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/app-activity", {
          params: { query: { days } },
        })
      ),
  })
}

/** Latest audit events. Not windowed: "recent" means recent, whatever the scope. */
export function useRecentActivity(enabled: boolean, pageSize = 8) {
  return useQuery({
    queryKey: [...dashboardKeys.recentActivity, pageSize],
    enabled,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: { query: { pageNumber: 1, pageSize } },
        })
      ),
  })
}

/**
 * Organization count. Platform admins see every organization via the paged
 * endpoint (asking for one row purely to read `totalCount`); everyone else sees
 * only the organizations they belong to.
 */
export function useOrganizationCount(canReadAll: boolean) {
  const all = useQuery({
    queryKey: dashboardKeys.organizations("all"),
    enabled: canReadAll,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/all", {
          params: { query: { pageNumber: 1, pageSize: 1 } },
        })
      ),
  })

  const mine = useQuery({
    queryKey: dashboardKeys.organizations("membership"),
    enabled: !canReadAll,
    queryFn: () => unwrap(api.GET("/api/v1/Organizations")),
  })

  return canReadAll
    ? { count: all.data?.totalCount, isLoading: all.isLoading }
    : { count: mine.data?.length, isLoading: mine.isLoading }
}
