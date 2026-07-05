import { describe, expect, it } from "vitest"

import {
  bucketDailyUtc,
  buildCountSeries,
  buildLoginSeries,
  daysUntil,
  pctDelta,
  rollupWeeklyCounts,
  rollupWeeklyLogins,
  successRate,
  topNWithOther,
  trailingUtcDays,
  utcDayKey,
  weekStartUtc,
} from "./helpers"

describe("utcDayKey", () => {
  it("takes the UTC calendar day from an ISO timestamp", () => {
    expect(utcDayKey("2026-06-23T23:59:59.99Z")).toBe("2026-06-23")
    expect(utcDayKey("2026-06-23T00:00:00")).toBe("2026-06-23")
  })
})

describe("trailingUtcDays", () => {
  it("produces a continuous inclusive window ending at endDay", () => {
    expect(trailingUtcDays(3, "2026-07-01")).toEqual([
      "2026-06-29",
      "2026-06-30",
      "2026-07-01",
    ])
  })

  it("crosses month boundaries", () => {
    expect(trailingUtcDays(2, "2026-07-01")).toEqual(["2026-06-30", "2026-07-01"])
  })
})

describe("buildLoginSeries", () => {
  it("zero-fills missing days and maps counts by UTC day", () => {
    const series = buildLoginSeries(
      [{ date: "2026-06-30T00:00:00", successCount: 6, failureCount: 1 }],
      3,
      "2026-07-01"
    )
    expect(series).toEqual([
      { day: "2026-06-29", success: 0, failure: 0 },
      { day: "2026-06-30", success: 6, failure: 1 },
      { day: "2026-07-01", success: 0, failure: 0 },
    ])
  })

  it("ignores rows outside the window", () => {
    const series = buildLoginSeries(
      [{ date: "2026-01-01T00:00:00", successCount: 99, failureCount: 9 }],
      2,
      "2026-07-01"
    )
    expect(series.every((p) => p.success === 0 && p.failure === 0)).toBe(true)
  })

  it("coerces string numerics from the API", () => {
    const series = buildLoginSeries(
      [{ date: "2026-07-01T00:00:00", successCount: "4", failureCount: "2" }],
      1,
      "2026-07-01"
    )
    expect(series[0]).toEqual({ day: "2026-07-01", success: 4, failure: 2 })
  })
})

describe("buildCountSeries / bucketDailyUtc", () => {
  it("zero-fills and sums per day", () => {
    const series = buildCountSeries(
      [{ date: "2026-07-01T00:00:00", count: 3 }],
      2,
      "2026-07-01"
    )
    expect(series).toEqual([
      { day: "2026-06-30", count: 0 },
      { day: "2026-07-01", count: 3 },
    ])
  })

  it("buckets raw timestamps into UTC days", () => {
    const series = bucketDailyUtc(
      ["2026-07-01T05:00:00Z", "2026-07-01T23:00:00Z", null, "2026-06-30T12:00:00Z"],
      2,
      "2026-07-01"
    )
    expect(series).toEqual([
      { day: "2026-06-30", count: 1 },
      { day: "2026-07-01", count: 2 },
    ])
  })
})

describe("weekStartUtc", () => {
  it("maps any day to its UTC Monday", () => {
    // 2026-07-01 is a Wednesday; its week starts Monday 2026-06-29.
    expect(weekStartUtc("2026-07-01")).toBe("2026-06-29")
    expect(weekStartUtc("2026-06-29")).toBe("2026-06-29")
    // Sunday belongs to the week started the previous Monday.
    expect(weekStartUtc("2026-07-05")).toBe("2026-06-29")
  })
})

describe("weekly rollups", () => {
  it("sums login outcomes into Monday-keyed weeks", () => {
    const weekly = rollupWeeklyLogins([
      { day: "2026-06-29", success: 1, failure: 1 },
      { day: "2026-07-01", success: 2, failure: 0 },
      { day: "2026-07-06", success: 5, failure: 3 },
    ])
    expect(weekly).toEqual([
      { day: "2026-06-29", success: 3, failure: 1 },
      { day: "2026-07-06", success: 5, failure: 3 },
    ])
  })

  it("sums plain counts into Monday-keyed weeks", () => {
    const weekly = rollupWeeklyCounts([
      { day: "2026-06-29", count: 1 },
      { day: "2026-07-05", count: 2 },
    ])
    expect(weekly).toEqual([{ day: "2026-06-29", count: 3 }])
  })
})

describe("successRate", () => {
  it("computes a one-decimal percentage", () => {
    expect(successRate(33, 8)).toBe(80.5)
    expect(successRate(1, 0)).toBe(100)
  })

  it("returns null when there were no attempts", () => {
    expect(successRate(0, 0)).toBeNull()
  })
})

describe("pctDelta", () => {
  it("computes a whole-percent change", () => {
    expect(pctDelta(12, 10)).toBe(20)
    expect(pctDelta(8, 10)).toBe(-20)
  })

  it("returns null without a baseline", () => {
    expect(pctDelta(5, 0)).toBeNull()
  })
})

describe("topNWithOther", () => {
  const merge = (rest: { label: string; value: number }[]) => ({
    label: "Other",
    value: rest.reduce((t, r) => t + r.value, 0),
  })

  it("returns rows unchanged when within the cap", () => {
    const rows = [{ label: "a", value: 1 }]
    expect(topNWithOther(rows, 2, merge)).toEqual(rows)
  })

  it("folds the tail into a merged row", () => {
    const rows = [
      { label: "a", value: 5 },
      { label: "b", value: 3 },
      { label: "c", value: 2 },
      { label: "d", value: 1 },
    ]
    expect(topNWithOther(rows, 2, merge)).toEqual([
      { label: "a", value: 5 },
      { label: "b", value: 3 },
      { label: "Other", value: 3 },
    ])
  })
})

describe("daysUntil", () => {
  it("returns null for empty or invalid values", () => {
    expect(daysUntil(null)).toBeNull()
    expect(daysUntil("not-a-date")).toBeNull()
  })

  it("returns a positive count for future instants", () => {
    const future = new Date(Date.now() + 10 * 86_400_000).toISOString()
    const days = daysUntil(future)
    expect(days).toBeGreaterThanOrEqual(9)
    expect(days).toBeLessThanOrEqual(10)
  })
})
