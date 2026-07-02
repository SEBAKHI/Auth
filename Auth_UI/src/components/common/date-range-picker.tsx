import { format, parseISO } from "date-fns"
import { CalendarIcon } from "lucide-react"
import type { DateRange } from "react-day-picker"
import { useTranslation } from "react-i18next"

import { Button } from "@/components/ui/button"
import { Calendar } from "@/components/ui/calendar"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { formatDate } from "@/lib/format"
import { cn } from "@/lib/utils"

function toDate(value?: string): Date | undefined {
  if (!value) return undefined
  const date = parseISO(value)
  return Number.isNaN(date.getTime()) ? undefined : date
}

/**
 * Calendar-based date range picker. Emits inclusive `from`/`to` as local
 * `yyyy-MM-dd` strings (or `undefined` when cleared).
 */
export function DateRangePicker({
  from,
  to,
  onChange,
  placeholder,
  className,
}: {
  from?: string
  to?: string
  onChange: (range: { from?: string; to?: string }) => void
  placeholder?: string
  className?: string
}) {
  const { t } = useTranslation()

  const selected: DateRange | undefined =
    from || to ? { from: toDate(from), to: toDate(to) } : undefined

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
          numberOfMonths={1}
          autoFocus
          selected={selected}
          defaultMonth={selected?.from}
          onSelect={(range) =>
            onChange({
              from: range?.from ? format(range.from, "yyyy-MM-dd") : undefined,
              to: range?.to ? format(range.to, "yyyy-MM-dd") : undefined,
            })
          }
        />
        <div className="flex justify-end border-t p-2">
          <Button
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
