import { TZDate } from "@date-fns/tz"
import { format, formatDistanceToNow, parseISO } from "date-fns"

import { getActiveTimeZone } from "@/lib/timezone"

/** Badge variants exposed by the shadcn Badge component (preset-styled). */
export type BadgeVariant = "default" | "secondary" | "destructive" | "outline"

// Datetime string carrying neither a "Z" nor a numeric offset. The API emits
// UTC with "Z"; this is a belt-and-braces guard for older cached payloads.
const DATETIME_WITHOUT_OFFSET = /^\d{4}-\d{2}-\d{2}T[\d:.]+$/

// Pure calendar date ("2026-07-04") — rendered as-is, never zone-shifted.
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/

function toDate(value: string | null | undefined): Date | null {
  if (!value) return null
  try {
    const iso = DATETIME_WITHOUT_OFFSET.test(value) ? `${value}Z` : value
    const date = parseISO(iso)
    return Number.isNaN(date.getTime()) ? null : date
  } catch {
    return null
  }
}

/** The instant re-expressed in the user's active display time zone. */
function inActiveZone(date: Date): TZDate {
  return new TZDate(date, getActiveTimeZone())
}

/** Absolute date-time, e.g. "21 Jun 2026, 14:05". Empty values render as an em dash. */
export function formatDateTime(value: string | null | undefined): string {
  const date = toDate(value)
  return date ? format(inActiveZone(date), "dd MMM yyyy, HH:mm") : "—"
}

/** Date only, e.g. "21 Jun 2026". */
export function formatDate(value: string | null | undefined): string {
  if (value && DATE_ONLY.test(value)) {
    const date = toDate(value)
    return date ? format(date, "dd MMM yyyy") : "—"
  }
  const date = toDate(value)
  return date ? format(inActiveZone(date), "dd MMM yyyy") : "—"
}

/** Relative time, e.g. "3 hours ago". */
export function formatRelative(value: string | null | undefined): string {
  const date = toDate(value)
  return date ? formatDistanceToNow(date, { addSuffix: true }) : "—"
}

// ─── UserStatus (Domain enum: Active=1, Inactive=2, Locked=3, Pending=4) ──────
const USER_STATUS: Record<number, { key: string; variant: BadgeVariant }> = {
  1: { key: "active", variant: "default" },
  2: { key: "inactive", variant: "secondary" },
  3: { key: "locked", variant: "destructive" },
  4: { key: "pending", variant: "outline" },
}

// The API serializes the enum as its member name (JsonStringEnumConverter), so
// resolve string names too — falling back to numeric/number-string values.
const USER_STATUS_NAME: Record<string, number> = {
  active: 1,
  inactive: 2,
  locked: 3,
  pending: 4,
}

export function userStatusMeta(status: number | string | undefined): {
  key: string
  variant: BadgeVariant
} {
  let code: number | undefined
  if (typeof status === "number") {
    code = status
  } else if (typeof status === "string") {
    code = /^\d+$/.test(status)
      ? Number(status)
      : USER_STATUS_NAME[status.toLowerCase()]
  }
  return USER_STATUS[code ?? -1] ?? { key: "unknown", variant: "outline" }
}

// ─── SecretStatus (enum: NotConfigured=0, Configured=1, Empty=2) ──────────────
const SECRET_STATUS: Record<number, { key: string; variant: BadgeVariant }> = {
  0: { key: "notConfigured", variant: "destructive" },
  1: { key: "configured", variant: "default" },
  2: { key: "empty", variant: "secondary" },
}

export function secretStatusMeta(status: number | string | undefined): {
  key: string
  variant: BadgeVariant
} {
  const code = typeof status === "string" ? Number(status) : status
  return SECRET_STATUS[code ?? -1] ?? { key: "unknown", variant: "outline" }
}

/** Build a "First Last" display name with sensible fallbacks. */
export function fullName(
  first: string | null | undefined,
  last: string | null | undefined,
  fallback = ""
): string {
  const name = [first, last].filter(Boolean).join(" ").trim()
  return name || fallback
}

/** Initials for an avatar fallback. */
export function initials(value: string | null | undefined): string {
  if (!value) return "?"
  const parts = value.trim().split(/\s+/).slice(0, 2)
  return parts.map((p) => p.charAt(0).toUpperCase()).join("") || "?"
}
