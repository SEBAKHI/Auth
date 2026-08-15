import { useTranslation } from "react-i18next"

import { FieldDescription } from "@authsystem/ui/field"

/**
 * Wraps a run in Unicode isolates so bidi never re-orders it.
 *
 * "1–100000" inside an Arabic sentence is a number-dash-number run with a
 * neutral character in the middle; left to the paragraph direction it can come
 * out reversed. `dir="ltr"` on the element cannot help, because the numbers and
 * the surrounding Arabic label share one text node once i18next interpolates.
 */
const isolateLtr = (text: string | number) => `⁦${text}⁩`

/**
 * The bounds and the shipped default of one input, stated before the user
 * types rather than after a rejected save.
 *
 * Every numeric control in the console already knows its own limits — the
 * registry carries them for system settings, the dialogs declare them
 * elsewhere — and none of them said so. An administrator learned the ceiling by
 * hitting it. This is the single place that answers "what may I put here", so
 * the wording is identical on every screen.
 *
 * Renders nothing when there is nothing to state: an empty line under a field
 * reads as a missing value, which is worse than no line at all.
 */
export function FieldConstraints({
  min,
  max,
  defaultValue,
  className,
}: {
  /**
   * Accepts a string as well as a number: the generated API types widen an
   * int64 to `number | string`, and forcing every caller to coerce would put
   * the same three lines in six files.
   */
  min?: number | string | null
  max?: number | string | null
  /**
   * The value the system ships with. Pass it only where one genuinely exists:
   * a create dialog has one, an edit dialog does not — its value is whatever
   * was saved, and calling that "the default" would be an invention.
   */
  defaultValue?: unknown
  className?: string
}) {
  const { t } = useTranslation()

  const lower = toBound(min)
  const upper = toBound(max)

  const bound = lower !== null && upper !== null
    ? t("common.range", { range: isolateLtr(`${lower}–${upper}`) })
    : lower !== null
      ? t("common.rangeMin", { min: isolateLtr(lower) })
      : upper !== null
        ? t("common.rangeMax", { max: isolateLtr(upper) })
        : null

  const shipped = renderDefault(defaultValue, {
    enabled: t("common.enabled"),
    disabled: t("common.disabled"),
  })

  const parts = [
    bound,
    shipped === null ? null : t("common.defaultValue", { value: isolateLtr(shipped) }),
  ].filter((part): part is string => Boolean(part))

  if (parts.length === 0) {
    return null
  }

  return (
    <FieldDescription data-slot="field-constraints" className={className}>
      {parts.join(" · ")}
    </FieldDescription>
  )
}

/** A bound as a finite number, or null when there is none to state. */
function toBound(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === "") return null
  const parsed = typeof value === "number" ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

/**
 * The default as a person reads it, or null when there is none to show.
 *
 * `false` is a real default and must survive the falsy check that would
 * swallow it; an empty array or empty string is an absent default rather than
 * a default of "nothing".
 */
function renderDefault(
  value: unknown,
  booleanLabels: { enabled: string; disabled: string }
): string | null {
  if (value === null || value === undefined) return null
  if (typeof value === "boolean") return value ? booleanLabels.enabled : booleanLabels.disabled
  if (Array.isArray(value)) return value.length > 0 ? value.join(", ") : null
  if (typeof value === "object") return null

  const text = String(value)
  return text.length > 0 ? text : null
}
