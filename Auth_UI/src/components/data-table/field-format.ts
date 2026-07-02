import type { TFunction } from "i18next"

import { formatDate, formatDateTime } from "@/lib/format"

export const ISO_DATETIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/
export const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

/** Verification/audit fields, always grouped at the bottom of the detail panel. */
export const DEFAULT_AUDIT_FIELD_KEYS = [
  "createdAt",
  "createdBy",
  "modifiedAt",
  "modifiedBy",
  "updatedAt",
  "updatedBy",
] as const

/** "phoneNumber" → "Phone number", "twoFactorEnabled" → "Two factor enabled". */
export function humanizeKey(key: string): string {
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/**
 * Best-effort generic rendering for a record field value. Shared by the
 * auto-discovered columns, the row-detail panel, and the CSV exporter so the
 * three stay consistent. Always returns a plain string (em dash for empties).
 */
export function formatFieldValue(value: unknown, t: TFunction): string {
  if (value === null || value === undefined || value === "") return "—"
  if (typeof value === "boolean") return value ? t("common.yes") : t("common.no")
  if (typeof value === "number") return value.toLocaleString()
  if (Array.isArray(value)) return value.length === 0 ? "—" : String(value.length)
  if (typeof value === "object") return "—"
  const text = String(value)
  if (ISO_DATETIME.test(text)) return formatDateTime(text)
  if (ISO_DATE.test(text)) return formatDate(text)
  return text
}
