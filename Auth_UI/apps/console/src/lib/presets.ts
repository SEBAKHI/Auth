import { useTranslation } from "react-i18next"

import type { Preset } from "@astoom/ui/common/preset-field"

/**
 * Domain preset sets for `PresetField`, so the common answer is one click and the
 * same choices appear everywhere a setting is edited.
 *
 * Values are the strings the forms already hold, and every set is bounded by what
 * the server accepts — offering a choice the API rejects would be worse than a
 * blank box.
 */

/**
 * Deployment names. Free text on the wire (`NotEmpty`, max 50), but in practice
 * these three, and both key dialogs already defaulted to `production`.
 */
export const ENVIRONMENTS: Preset[] = [
  { value: "development", label: "development" },
  { value: "staging", label: "staging" },
  { value: "production", label: "production" },
]

/** Server: non-nullable `int`, `GreaterThan(0)`; its own default is 60. */
export const RATE_PER_MINUTE: Preset[] = [
  { value: "60", label: "60" },
  { value: "300", label: "300" },
  { value: "1000", label: "1000" },
  { value: "6000", label: "6000" },
]

/** Server: non-nullable `int`, `GreaterThan(0)`; its own default is 10000. */
export const RATE_PER_DAY: Preset[] = [
  { value: "10000", label: "10000" },
  { value: "100000", label: "100000" },
  { value: "1000000", label: "1000000" },
]

/**
 * Coerce the grace-period field to minutes.
 *
 * Zero is a real choice ("Immediate"), so it must survive: the previous
 * `Number(grace) || 60` turned a deliberate 0 into 60, silently keeping the old
 * key alive for an hour after a rotation the operator wanted to take effect now.
 */
export function toGracePeriod(value: string, fallback = 60): number {
  if (value.trim().length === 0) return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback
}

/**
 * Rotation grace period in minutes. Server allows `GreaterThanOrEqualTo(0)`, so
 * 0 is legal and means the old key stops working at once.
 */
export function useGracePeriodPresets(): Preset[] {
  const { t } = useTranslation()
  return [
    { value: "0", label: t("common.immediate") },
    { value: "5", label: t("common.minutesShort", { count: 5 }) },
    { value: "60", label: t("common.hoursShort", { count: 1 }) },
    { value: "1440", label: t("common.hoursShort", { count: 24 }) },
  ]
}
