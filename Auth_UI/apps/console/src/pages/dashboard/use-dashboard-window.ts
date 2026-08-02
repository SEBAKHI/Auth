import * as React from "react"
import { useSearchParams } from "react-router-dom"

import { useActiveTimeZone } from "@authsystem/i18n/timezone"

/**
 * The server validates `days` as `InclusiveBetween(1, 90)` (SharedValidationRules),
 * and the dashboard endpoints accept only a day count — there is no from/to range.
 * So "custom" here means a custom **number of days**, not an arbitrary date range.
 */
export const MIN_DAYS = 1
export const MAX_DAYS = 90
export const DEFAULT_DAYS = 30

/** The presets offered before anyone has to type a number. */
export const DAY_PRESETS = [7, 14, 30, 90] as const

export type Granularity = "daily" | "weekly"

export function clampDays(value: number): number {
  if (!Number.isFinite(value)) return DEFAULT_DAYS
  return Math.min(MAX_DAYS, Math.max(MIN_DAYS, Math.round(value)))
}

/**
 * The dashboard's single scope, held in the URL so a view is linkable and the
 * Back button works.
 *
 * One window governs every card. The page previously mixed three undisclosed
 * windows — 30-day aggregates, a 14-day audit series and a 7-day event KPI — so
 * two numbers sitting side by side were measuring different periods.
 */
export function useDashboardWindow() {
  const [params, setParams] = useSearchParams()
  const timeZone = useActiveTimeZone()

  const days = clampDays(Number(params.get("days") ?? DEFAULT_DAYS))
  const granularity: Granularity =
    params.get("grain") === "weekly" ? "weekly" : "daily"
  const tab = params.get("tab") ?? "overview"

  const update = React.useCallback(
    (next: { days?: number; granularity?: Granularity; tab?: string }) => {
      setParams(
        (current) => {
          const merged = new URLSearchParams(current)
          if (next.days !== undefined) {
            const clamped = clampDays(next.days)
            if (clamped === DEFAULT_DAYS) merged.delete("days")
            else merged.set("days", String(clamped))
          }
          if (next.granularity !== undefined) {
            if (next.granularity === "daily") merged.delete("grain")
            else merged.set("grain", next.granularity)
          }
          if (next.tab !== undefined) {
            if (next.tab === "overview") merged.delete("tab")
            else merged.set("tab", next.tab)
          }
          return merged
        },
        { replace: true }
      )
    },
    [setParams]
  )

  return { days, granularity, tab, timeZone, update }
}

/**
 * Query keys for the dashboard aggregates.
 *
 * Every key carries the window, so changing it refetches rather than showing a
 * number from a different period. Time zone is part of the key for the endpoints
 * that bucket by calendar day, because the buckets differ per zone.
 */
export const dashboardKeys = {
  userStats: (days: number, timeZone: string) =>
    ["dashboard", "user-stats", days, timeZone] as const,
  authStats: (days: number, timeZone: string) =>
    ["dashboard", "auth-stats", days, timeZone] as const,
  auditStats: (days: number, timeZone: string) =>
    ["dashboard", "audit-stats", days, timeZone] as const,
  sessionStats: (days: number) =>
    ["dashboard", "session-stats", days] as const,
  appActivity: (days: number) => ["dashboard", "app-activity", days] as const,
  organizations: (scope: "all" | "membership") =>
    ["dashboard", "orgs", scope] as const,
  recentActivity: ["dashboard", "recent"] as const,
}
