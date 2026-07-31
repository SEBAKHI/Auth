import { CalendarIcon } from "lucide-react"
import type { DateRange } from "react-day-picker"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { Calendar } from "@authsystem/ui/calendar"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@authsystem/ui/popover"
import { Separator } from "@authsystem/ui/separator"
import { DEFAULT_DATE_BOUNDS } from "@authsystem/ui/common/date-picker"
import { formatDate, parseCalendarDate, toCalendarDate } from "@authsystem/ui/format"
import { cn } from "@authsystem/ui/utils"

/**
 * Calendar-based date range picker. Emits inclusive `from`/`to` as local
 * `yyyy-MM-dd` strings (or `undefined` when cleared).
 *
 * Shares the month+year dropdown caption with `DatePicker` so the two read as one
 * control; see that file for why `startMonth`/`endMonth` are set explicitly.
 */
export function DateRangePicker({
  from,
  to,
  onChange,
  startMonth,
  endMonth,
  placeholder,
  className,
}: {
  from?: string
  to?: string
  onChange: (range: { from?: string; to?: string }) => void
  startMonth?: Date
  endMonth?: Date
  placeholder?: string
  className?: string
}) {
  const { t } = useTranslation()

  const selected: DateRange | undefined =
    from || to
      ? { from: parseCalendarDate(from), to: parseCalendarDate(to) }
      : undefined

  const label =
    from && to
      ? `${formatDate(from)} – ${formatDate(to)}`
      : from
        ? formatDate(from)
        : (placeholder ?? t("common.selectDateRange"))

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          className={cn(
            "w-full justify-start font-normal",
            !from && !to && "text-muted-foreground",
            className
          )}
        >
          <CalendarIcon data-icon="inline-start" />
          {label}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="range"
          captionLayout="dropdown"
          numberOfMonths={1}
          autoFocus
          selected={selected}
          defaultMonth={selected?.from}
          startMonth={startMonth ?? DEFAULT_DATE_BOUNDS.startMonth}
          endMonth={endMonth ?? DEFAULT_DATE_BOUNDS.endMonth}
          onSelect={(range) =>
            onChange({
              from: range?.from ? toCalendarDate(range.from) : undefined,
              to: range?.to ? toCalendarDate(range.to) : undefined,
            })
          }
        />
        <Separator />
        <div className="flex justify-end p-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => onChange({ from: undefined, to: undefined })}
          >
            {t("common.clear")}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
