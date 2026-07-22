import type { Schemas } from "@astoom/api/types"
import { toNumber } from "@astoom/api/helpers"

type DailyLoginCount = Schemas["DailyLoginCountDto"]
type DailyCount = Schemas["DailyCountDto"]

/** One viewer-local calendar day of login outcomes. */
export type LoginPoint = { day: string; success: number; failure: number }
/** One viewer-local calendar day of a single count. */
export type CountPoint = { day: string; count: number }

const DAY_MS = 86_400_000

/** Calendar-day key ("yyyy-MM-dd") for an instant in an IANA time zone. */
export function calendarDayKey(value: string | Date, timeZone: string): string {
  const date = typeof value === "string" ? new Date(value) : value
  const parts = new Intl.DateTimeFormat("en", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(date)

  const year = parts.find((part) => part.type === "year")?.value
  const month = parts.find((part) => part.type === "month")?.value
  const day = parts.find((part) => part.type === "day")?.value
  if (!year || !month || !day) throw new RangeError("Invalid calendar day")
  return `${year}-${month}-${day}`
}

/** Today's calendar day in the viewer time zone. */
export function todayInTimeZone(timeZone: string, now = new Date()): string {
  return calendarDayKey(now, timeZone)
}

/** The trailing calendar-day keys ending at `endDay` (inclusive). */
export function trailingCalendarDays(days: number, endDay: string): string[] {
  const end = Date.parse(`${endDay}T00:00:00Z`)
  const keys: string[] = []
  for (let i = days - 1; i >= 0; i--) {
    keys.push(new Date(end - i * DAY_MS).toISOString().slice(0, 10))
  }
  return keys
}

/** Zero-fill server-produced local-day login buckets into a continuous series. */
export function buildLoginSeries(
  rows: DailyLoginCount[],
  days: number,
  timeZone: string,
  endDay = todayInTimeZone(timeZone)
): LoginPoint[] {
  const byDay = new Map<string, LoginPoint>()
  for (const key of trailingCalendarDays(days, endDay)) {
    byDay.set(key, { day: key, success: 0, failure: 0 })
  }
  for (const row of rows) {
    if (!row.date) continue
    const point = byDay.get(row.date.slice(0, 10))
    if (point) {
      point.success += toNumber(row.successCount)
      point.failure += toNumber(row.failureCount)
    }
  }
  return [...byDay.values()]
}

/** Zero-fill server-produced local-day count buckets into a continuous series. */
export function buildCountSeries(
  rows: DailyCount[],
  days: number,
  timeZone: string,
  endDay = todayInTimeZone(timeZone)
): CountPoint[] {
  const byDay = new Map<string, CountPoint>()
  for (const key of trailingCalendarDays(days, endDay)) {
    byDay.set(key, { day: key, count: 0 })
  }
  for (const row of rows) {
    if (!row.date) continue
    const point = byDay.get(row.date.slice(0, 10))
    if (point) point.count += toNumber(row.count)
  }
  return [...byDay.values()]
}

/** Bucket raw ISO timestamps into viewer-local calendar days. */
export function bucketDaily(
  timestamps: Array<string | null | undefined>,
  days: number,
  timeZone: string,
  endDay = todayInTimeZone(timeZone)
): CountPoint[] {
  const byDay = new Map<string, CountPoint>()
  for (const key of trailingCalendarDays(days, endDay)) {
    byDay.set(key, { day: key, count: 0 })
  }
  for (const ts of timestamps) {
    if (!ts) continue
    const point = byDay.get(calendarDayKey(ts, timeZone))
    if (point) point.count += 1
  }
  return [...byDay.values()]
}

/** Monday ("yyyy-MM-dd") of the week containing a calendar-day key. */
export function weekStartCalendar(day: string): string {
  const t = Date.parse(`${day}T00:00:00Z`)
  const sinceMonday = (new Date(t).getUTCDay() + 6) % 7
  return new Date(t - sinceMonday * DAY_MS).toISOString().slice(0, 10)
}

/**
 * Sum a daily login series into calendar weeks (keyed by their Monday).
 * Valid because attempt counts are additive; do NOT use for distinct-user
 * series, where summing days over-counts.
 */
export function rollupWeeklyLogins(points: LoginPoint[]): LoginPoint[] {
  const byWeek = new Map<string, LoginPoint>()
  for (const point of points) {
    const week = weekStartCalendar(point.day)
    const acc = byWeek.get(week) ?? { day: week, success: 0, failure: 0 }
    acc.success += point.success
    acc.failure += point.failure
    byWeek.set(week, acc)
  }
  return [...byWeek.values()]
}

/** Sum a daily count series into calendar weeks (keyed by their Monday). */
export function rollupWeeklyCounts(points: CountPoint[]): CountPoint[] {
  const byWeek = new Map<string, CountPoint>()
  for (const point of points) {
    const week = weekStartCalendar(point.day)
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
