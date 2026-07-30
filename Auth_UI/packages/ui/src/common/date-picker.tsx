import * as React from "react"
import { CalendarIcon } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import { Calendar } from "@astoom/ui/calendar"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@astoom/ui/popover"
import { Separator } from "@astoom/ui/separator"
import { formatDate, parseCalendarDate, toCalendarDate } from "@astoom/ui/format"
import { cn } from "@astoom/ui/utils"

/** Default selectable span when a caller does not narrow it. */
const DEFAULT_YEARS_BACK = 10
const DEFAULT_YEARS_FORWARD = 10

/**
 * A month `yearsFromNow` away, for `startMonth`/`endMonth` bounds. Exported so
 * every date surface bounds its year dropdown the same way.
 */
export function monthsFromNow(yearsFromNow: number): Date {
  const date = new Date()
  date.setFullYear(date.getFullYear() + yearsFromNow)
  return date
}

/** Bounds shared by pickers that do not narrow the span themselves. */
export const DEFAULT_DATE_BOUNDS = {
  get startMonth() {
    return monthsFromNow(-DEFAULT_YEARS_BACK)
  },
  get endMonth() {
    return monthsFromNow(DEFAULT_YEARS_FORWARD)
  },
}

/**
 * Single-date picker: a Popover-hosted `Calendar` with **month and year
 * dropdowns** (`captionLayout="dropdown"`), which is the project's only
 * date-entry idiom. It replaces `<Input type="date" />` — the native control
 * renders browser chrome that does not match the preset.
 *
 * Values are exchanged as `yyyy-MM-dd` strings, so it drops straight into a
 * react-hook-form `FormField` wherever a native date input used to sit.
 *
 * `minDate`/`maxDate` are worth setting per use. They bound the year dropdown
 * *and* grey out the days outside the range, so an expiry field cannot be given a
 * date in the past. Without bounds react-day-picker v10 opens the year dropdown
 * on a 100-year span (v10 removed `fromYear`/`toYear`; `startMonth`/`endMonth`
 * are the replacements, and are derived from these).
 */
export function DatePicker({
  value,
  onChange,
  minDate,
  maxDate,
  placeholder,
  disabled,
  clearable = true,
  id,
  className,
  "aria-invalid": ariaInvalid,
}: {
  value?: string
  onChange: (value?: string) => void
  /** Earliest selectable day; also the first month in the dropdown. */
  minDate?: Date
  /** Latest selectable day; also the last month in the dropdown. */
  maxDate?: Date
  placeholder?: string
  disabled?: boolean
  /** Show the footer "Clear" action. Off for fields that must hold a date. */
  clearable?: boolean
  id?: string
  className?: string
  "aria-invalid"?: boolean
}) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)

  const selected = parseCalendarDate(value)

  // Only include the bounds that were actually given: react-day-picker matchers
  // treat a present-but-undefined `before`/`after` as a malformed matcher.
  const outOfRange = [
    ...(minDate ? [{ before: minDate }] : []),
    ...(maxDate ? [{ after: maxDate }] : []),
  ]

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          disabled={disabled}
          aria-invalid={ariaInvalid}
          className={cn(
            "w-full justify-start font-normal",
            !selected && "text-muted-foreground",
            className
          )}
        >
          <CalendarIcon data-icon="inline-start" />
          {selected ? formatDate(value) : (placeholder ?? t("common.selectDate"))}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          captionLayout="dropdown"
          autoFocus
          selected={selected}
          defaultMonth={selected}
          startMonth={minDate ?? DEFAULT_DATE_BOUNDS.startMonth}
          endMonth={maxDate ?? DEFAULT_DATE_BOUNDS.endMonth}
          disabled={outOfRange.length > 0 ? outOfRange : undefined}
          onSelect={(date) => {
            onChange(date ? toCalendarDate(date) : undefined)
            if (date) setOpen(false)
          }}
        />
        {clearable ? (
          <>
            <Separator />
            <div className="flex justify-end p-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => {
                  onChange(undefined)
                  setOpen(false)
                }}
              >
                {t("common.clear")}
              </Button>
            </div>
          </>
        ) : null}
      </PopoverContent>
    </Popover>
  )
}
