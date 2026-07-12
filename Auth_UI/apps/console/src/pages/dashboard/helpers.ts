import type { Schemas } from "@astoom/api/types"
import { toNumber } from "@astoom/api/helpers"

type DailyLoginCount = Schemas["DailyLoginCountDto"]
type DailyCount = Schemas["DailyCountDto"]

/** One UTC day of login outcomes, zero-filled across the window. */
export type LoginPoint = { day: string; success: number; failure: number }
/** One UTC day of a single count, zero-filled across the window. */
export type CountPoint = { day: string; count: number }

const DAY_MS = 86_400_000

/** UTC calendar day ("yyyy-MM-dd") of an ISO timestamp. */
export function utcDayKey(iso: string): string {
  return iso.slice(0, 10)
}

/** Today's UTC day key. */
export function todayUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

/** The trailing `days` UTC day keys ending at `endDay` (inclusive). */
export function trailingUtcDays(days: number, endDay = todayUtc()): string[] {
  const end = Date.parse(`${endDay}T00:00:00Z`)
  const keys: string[] = []
  for (let i = days - 1; i >= 0; i--) {
    keys.push(new Date(end - i * DAY_MS).toISOString().slice(0, 10))
  }
  return keys
}

/** Zero-fill the API's per-day login outcomes into a continuous UTC-day series. */
export function buildLoginSeries(
  rows: DailyLoginCount[],
  days: number,
  endDay = todayUtc()
): LoginPoint[] {
  const byDay = new Map<string, LoginPoint>()
  for (const key of trailingUtcDays(days, endDay)) {
    byDay.set(key, { day: key, success: 0, failure: 0 })
  }
  for (const row of rows) {
    if (!row.date) continue
    const point = byDay.get(utcDayKey(row.date))
    if (point) {
      point.success += toNumber(row.successCount)
      point.failure += toNumber(row.failureCount)
    }
  }
  return [...byDay.values()]
}

/** Zero-fill the API's per-day counts into a continuous UTC-day series. */
export function buildCountSeries(
  rows: DailyCount[],
  days: number,
  endDay = todayUtc()
): CountPoint[] {
  const byDay = new Map<string, CountPoint>()
  for (const key of trailingUtcDays(days, endDay)) {
    byDay.set(key, { day: key, count: 0 })
  }
  for (const row of rows) {
    if (!row.date) continue
    const point = byDay.get(utcDayKey(row.date))
    if (point) point.count += toNumber(row.count)
  }
  return [...byDay.values()]
}

/** Bucket raw ISO timestamps into per-UTC-day counts across the window. */
export function bucketDailyUtc(
  timestamps: Array<string | null | undefined>,
  days: number,
  endDay = todayUtc()
): CountPoint[] {
  const byDay = new Map<string, CountPoint>()
  for (const key of trailingUtcDays(days, endDay)) {
    byDay.set(key, { day: key, count: 0 })
  }
  for (const ts of timestamps) {
    if (!ts) continue
    const point = byDay.get(utcDayKey(ts))
    if (point) point.count += 1
  }
  return [...byDay.values()]
}

/** UTC Monday ("yyyy-MM-dd") of the week containing the given UTC day. */
export function weekStartUtc(day: string): string {
  const t = Date.parse(`${day}T00:00:00Z`)
  const sinceMonday = (new Date(t).getUTCDay() + 6) % 7
  return new Date(t - sinceMonday * DAY_MS).toISOString().slice(0, 10)
}

/**
 * Sum a daily login series into UTC weeks (keyed by their Monday).
 * Valid because attempt counts are additive; do NOT use for distinct-user
 * series, where summing days over-counts.
 */
export function rollupWeeklyLogins(points: LoginPoint[]): LoginPoint[] {
  const byWeek = new Map<string, LoginPoint>()
  for (const point of points) {
    const week = weekStartUtc(point.day)
    const acc = byWeek.get(week) ?? { day: week, success: 0, failure: 0 }
    acc.success += point.success
    acc.failure += point.failure
    byWeek.set(week, acc)
  }
  return [...byWeek.values()]
}

/** Sum a daily count series into UTC weeks (keyed by their Monday). */
export function rollupWeeklyCounts(points: CountPoint[]): CountPoint[] {
  const byWeek = new Map<string, CountPoint>()
  for (const point of points) {
    const week = weekStartUtc(point.day)
    const acc = byWeek.get(week) ?? { day: week, count: 0 }
    acc.count += point.count
    byWeek.set(week, acc)
  }
  return [...byWeek.values()]
}

/** Success percentage (one decimal), or null when there were no attempts. */
export function successRate(success: number, failure: number): number | null {
  const total = success + failure
  if (total === 0) return null
  return Math.round((success / total) * 1000) / 10
}

/** Whole-percent change vs a previous value, or null when there is no baseline. */
export function pctDelta(current: number, previous: number): number | null {
  if (previous <= 0) return null
  return Math.round(((current - previous) / previous) * 100)
}

/** Keep the first `n` rows and fold the rest into a single merged row. */
export function topNWithOther<T>(
  rows: T[],
  n: number,
  merge: (rest: T[]) => T
): T[] {
  if (rows.length <= n) return rows
  return [...rows.slice(0, n), merge(rows.slice(n))]
}

/** Whole days from now until an ISO instant; null for empty values. */
export function daysUntil(iso: string | null | undefined): number | null {
  if (!iso) return null
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return null
  return Math.ceil((t - Date.now()) / DAY_MS)
}
