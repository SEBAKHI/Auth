import { TZDate } from "@date-fns/tz"
import { format, formatDistanceToNow, parseISO, type Locale } from "date-fns"
import { ar, faIR, fr, tr, zhCN } from "date-fns/locale"

import i18n from "@authsystem/i18n"
import { getActiveTimeZone } from "@authsystem/i18n/timezone"

// date-fns locale per UI language. English needs none, and date-fns ships no
// Urdu locale, so "en" and "ur" use the default English date formatting.
const DATE_LOCALES: Record<string, Locale> = { ar, tr, fr, zh: zhCN, fa: faIR }

/** The date-fns locale matching the active UI language (undefined = English). */
export function activeDateLocale(): Locale | undefined {
  return DATE_LOCALES[i18n.language]
}

/** BCP-47 tag for number formatting: active language with Latin digits. */
export function numberLocale(): string {
  return `${i18n.language}-u-nu-latn`
}

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
  return date
    ? format(inActiveZone(date), "dd MMM yyyy, HH:mm", {
        locale: activeDateLocale(),
      })
    : "—"
}

/** Date only, e.g. "21 Jun 2026". */
export function formatDate(value: string | null | undefined): string {
  if (value && DATE_ONLY.test(value)) {
    const date = toDate(value)
    return date
      ? format(date, "dd MMM yyyy", { locale: activeDateLocale() })
      : "—"
  }
  const date = toDate(value)
  return date
    ? format(inActiveZone(date), "dd MMM yyyy", { locale: activeDateLocale() })
    : "—"
}

/**
 * Parse a calendar-date value for the Calendar component. A date-only value is a
 * calendar day, not an instant, so it is deliberately NOT re-expressed in the
 * display time zone — shifting it would move the highlighted day by one.
 */
export function parseCalendarDate(
  value: string | null | undefined
): Date | undefined {
  return toDate(value) ?? undefined
}

/** Serialize a Date to the `yyyy-MM-dd` wire format the date fields exchange. */
export function toCalendarDate(date: Date): string {
  return format(date, "yyyy-MM-dd")
}

/** Relative time, e.g. "3 hours ago". */
export function formatRelative(value: string | null | undefined): string {
  const date = toDate(value)
  return date
    ? formatDistanceToNow(date, { addSuffix: true, locale: activeDateLocale() })
    : "—"
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
//
// Keyed by BOTH the numeric value and the serialized name. The API serializes
// enums as string names, so the previous number-only lookup ran
// Number("Configured") -> NaN and fell through to "unknown" for every secret on
// the page — a status display that could never once show a real status.
const SECRET_STATUS: Record<string, { key: string; variant: BadgeVariant }> = {
  0: { key: "notConfigured", variant: "destructive" },
  1: { key: "configured", variant: "default" },
  2: { key: "empty", variant: "secondary" },
  notconfigured: { key: "notConfigured", variant: "destructive" },
  configured: { key: "configured", variant: "default" },
  empty: { key: "empty", variant: "secondary" },
}

export function secretStatusMeta(status: number | string | undefined): {
  key: string
  variant: BadgeVariant
} {
  if (status === undefined || status === null) {
    return { key: "unknown", variant: "outline" }
  }

  // Accept the numeric form, a numeric string, and the enum name in any casing.
  const lookup = typeof status === "number" ? String(status) : status.trim().toLowerCase()
  return SECRET_STATUS[lookup] ?? { key: "unknown", variant: "outline" }
}

// ─── ApplicationAccessMode (enum: Everyone=1, Restricted=2) ───────────────────
//
// Who may sign in to an application that is switched on. Same two-shape problem
// as the statuses above: the API serializes the enum as its name, while the
// generated OpenAPI types call it a number — so anything comparing the raw value
// to a literal is wrong for one of the two forms.
export type ApplicationAccessMode = "Everyone" | "Restricted"

const ACCESS_MODE: Record<string, ApplicationAccessMode> = {
  1: "Everyone",
  2: "Restricted",
  everyone: "Everyone",
  restricted: "Restricted",
}

/**
 * Normalizes an access mode to its name. Unknown values read as
 * <c>Restricted</c>: guessing "open to everyone" from a value we failed to
 * understand would overstate access in the one place it must not be overstated.
 */
export function accessMode(
  value: number | string | undefined | null
): ApplicationAccessMode {
  if (value === undefined || value === null) return "Restricted"

  const lookup =
    typeof value === "number" ? String(value) : value.trim().toLowerCase()
  return ACCESS_MODE[lookup] ?? "Restricted"
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
