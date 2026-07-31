import { format, parseISO } from "date-fns"

import { activeDateLocale } from "@astoom/ui/format"

/**
 * Axis and tooltip label for a series bucket keyed `yyyy-MM-dd`.
 *
 * Shared so every time axis on the dashboard reads the same way, and so the date
 * locale follows the UI language in one place instead of per card.
 */
export function formatBucket(day: string, variant: "short" | "long" = "short") {
  const locale = activeDateLocale()
  try {
    return format(parseISO(day), variant === "long" ? "dd MMM yyyy" : "MMM d", {
      locale,
    })
  } catch {
    return day
  }
}
