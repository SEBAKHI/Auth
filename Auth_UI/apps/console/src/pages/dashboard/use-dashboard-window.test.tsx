import { act, renderHook } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it } from "vitest"

import { setActiveTimeZone } from "@authsystem/i18n/timezone"

import {
  DEFAULT_DAYS,
  MAX_DAYS,
  MIN_DAYS,
  clampDays,
  dashboardKeys,
  useDashboardWindow,
} from "./use-dashboard-window"

function wrapperAt(entry: string) {
  return function DashboardRouter({ children }: { children: React.ReactNode }) {
    return <MemoryRouter initialEntries={[entry]}>{children}</MemoryRouter>
  }
}

describe("clampDays", () => {
  it("rounds finite values into the server-supported interval", () => {
    expect(clampDays(14.6)).toBe(15)
    expect(clampDays(-10)).toBe(MIN_DAYS)
    expect(clampDays(200)).toBe(MAX_DAYS)
  })

  it("uses the documented default for non-finite input", () => {
    expect(clampDays(Number.NaN)).toBe(DEFAULT_DAYS)
    expect(clampDays(Number.POSITIVE_INFINITY)).toBe(DEFAULT_DAYS)
  })
})

describe("useDashboardWindow", () => {
  it("normalizes missing and stale URL state", () => {
    setActiveTimeZone("Etc/UTC")
    const { result } = renderHook(() => useDashboardWindow(), {
      wrapper: wrapperAt("/?days=not-a-number&grain=hourly&tab=retired"),
    })

    expect(result.current).toMatchObject({
      days: DEFAULT_DAYS,
      granularity: "daily",
      tab: "overview",
      timeZone: "Etc/UTC",
    })
  })

  it("reads supported deep-link state", () => {
    const { result } = renderHook(() => useDashboardWindow(), {
      wrapper: wrapperAt("/?days=7&grain=weekly&tab=security"),
    })

    expect(result.current).toMatchObject({
      days: 7,
      granularity: "weekly",
      tab: "security",
    })
  })

  it("writes non-default values and removes defaults without losing other parameters", () => {
    const { result } = renderHook(() => useDashboardWindow(), {
      wrapper: wrapperAt("/?keep=yes"),
    })

    act(() => {
      result.current.update({ days: 14, granularity: "weekly", tab: "audit" })
    })
    expect(result.current).toMatchObject({
      days: 14,
      granularity: "weekly",
      tab: "audit",
    })

    act(() => {
      result.current.update({
        days: DEFAULT_DAYS,
        granularity: "daily",
        tab: "overview",
      })
    })
    expect(result.current).toMatchObject({
      days: DEFAULT_DAYS,
      granularity: "daily",
      tab: "overview",
    })
  })
})

describe("dashboardKeys", () => {
  it("carries every parameter that changes a query result", () => {
    expect(dashboardKeys.userStats(7, "Etc/UTC")).toEqual([
      "dashboard",
      "user-stats",
      7,
      "Etc/UTC",
    ])
    expect(dashboardKeys.authStats(14, "Europe/Istanbul")).toEqual([
      "dashboard",
      "auth-stats",
      14,
      "Europe/Istanbul",
    ])
    expect(dashboardKeys.auditStats(30, "Etc/UTC")).toEqual([
      "dashboard",
      "audit-stats",
      30,
      "Etc/UTC",
    ])
    expect(dashboardKeys.sessionStats(7)).toEqual([
      "dashboard",
      "session-stats",
      7,
    ])
    expect(dashboardKeys.appActivity(90)).toEqual([
      "dashboard",
      "app-activity",
      90,
    ])
    expect(dashboardKeys.credentialStats).toEqual([
      "dashboard",
      "credential-stats",
    ])
    expect(dashboardKeys.organizations("membership")).toEqual([
      "dashboard",
      "orgs",
      "membership",
    ])
    expect(dashboardKeys.recentActivity).toEqual(["dashboard", "recent"])
  })
})
