import { format } from "date-fns"
import { useTranslation } from "react-i18next"

import { NativeSelect } from "@authsystem/ui/native-select"
import { activeDateLocale } from "@authsystem/ui/format"

/** Policy versions are `YYYY.MM`; the API and the list page both rely on it. */
const VERSION_RE = /^(\d{4})\.(\d{2})$/

/** How far the year list reaches around today. Policy revisions are near-term. */
const YEARS_BACK = 2
const YEARS_FORWARD = 3

function parseVersion(value: string): { year: number; month: number } | null {
  const match = VERSION_RE.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  if (month < 1 || month > 12) return null
  return { year, month }
}

function toVersion(year: number, month: number): string {
  return `${year}.${String(month).padStart(2, "0")}`
}

/**
 * Year + month pickers for a policy version.
 *
 * The field is validated by `/^\d{4}\.\d{2}$/` — a year-and-month picker in
 * disguise — so asking for free text only invited typos the operator then had to
 * decode from a validation message. Two selects cannot produce an invalid value.
 *
 * `NativeSelect` because every usage sits inside a dialog, where the menu must
 * stay within the dialog's own focus and pointer boundary.
 */
export function PolicyVersionField({
  value,
  onChange,
  disabled,
  id,
}: {
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  id?: string
}) {
  const { t } = useTranslation()
  const locale = activeDateLocale()

  const now = new Date()
  const parsed = parseVersion(value)
  const year = parsed?.year ?? now.getFullYear()
  const month = parsed?.month ?? now.getMonth() + 1

  const currentYear = now.getFullYear()
  const years: number[] = []
  for (let y = currentYear - YEARS_BACK; y <= currentYear + YEARS_FORWARD; y++) {
    years.push(y)
  }
  // Keep an out-of-range year already on the record selectable rather than
  // silently rewriting a historical version the moment the dialog opens.
  if (!years.includes(year)) years.push(year)
  years.sort((a, b) => a - b)

  const months = Array.from({ length: 12 }, (_, index) => ({
    value: index + 1,
    label: format(new Date(2000, index, 1), "LLLL", { locale }),
  }))

  return (
    // No `dir` here. These are two controls, not one string, and forcing `ltr`
    // made the pair an LTR island inside an RTL dialog: the year landed on the
    // physical left, so an Arabic reader met the month first, and both select
    // chevrons sat on the wrong edge because `inline-end` resolved to the right.
    // Inheriting the UI direction puts the year at the reading start in either
    // direction, which is the order the `YYYY.MM` value is written in.
    <div className="flex gap-2">
      <NativeSelect
        id={id}
        disabled={disabled}
        aria-label={t("notifications.policyVersionYear")}
        value={String(year)}
        onChange={(event) => onChange(toVersion(Number(event.target.value), month))}
      >
        {years.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </NativeSelect>
      <NativeSelect
        disabled={disabled}
        aria-label={t("notifications.policyVersionMonth")}
        value={String(month)}
        onChange={(event) => onChange(toVersion(year, Number(event.target.value)))}
      >
        {months.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </NativeSelect>
    </div>
  )
}
