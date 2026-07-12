import * as React from "react"
import { Check, ChevronsUpDown } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@astoom/ui/command"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@astoom/ui/popover"
import { ScrollArea } from "@astoom/ui/scroll-area"
import { cn } from "@astoom/ui/utils"

/**
 * Stored profile value meaning "automatic": dates render in the browser's
 * time zone. Users wanting literal UTC pick "Etc/UTC" from the IANA list.
 */
const AUTO_VALUE = "UTC"

interface ZoneOption {
  zone: string
  /** e.g. "UTC+03:00" — current offset, so DST is reflected. */
  offsetLabel: string
  offsetMinutes: number
}

/** Current UTC offset of a zone, e.g. "UTC+03:00" (empty when unresolvable). */
function zoneOffsetLabel(timeZone: string): string {
  try {
    const part = new Intl.DateTimeFormat("en", {
      timeZone,
      timeZoneName: "longOffset",
    })
      .formatToParts(new Date())
      .find((p) => p.type === "timeZoneName")?.value
    if (!part) return ""
    return part === "GMT" ? "UTC+00:00" : part.replace("GMT", "UTC")
  } catch {
    return ""
  }
}

function offsetToMinutes(offsetLabel: string): number {
  const match = /UTC([+-])(\d{2}):(\d{2})/.exec(offsetLabel)
  if (!match) return 0
  const sign = match[1] === "-" ? -1 : 1
  return sign * (Number(match[2]) * 60 + Number(match[3]))
}

/** IANA zones with their current offsets, sorted by offset then name. */
function buildZoneOptions(): ZoneOption[] {
  let zones: string[]
  try {
    zones = Intl.supportedValuesOf("timeZone")
  } catch {
    return []
  }

  return zones
    .map((zone) => {
      const offsetLabel = zoneOffsetLabel(zone)
      return { zone, offsetLabel, offsetMinutes: offsetToMinutes(offsetLabel) }
    })
    .sort(
      (a, b) =>
        a.offsetMinutes - b.offsetMinutes || a.zone.localeCompare(b.zone)
    )
}

function zoneDisplay(option: ZoneOption): string {
  return option.offsetLabel
    ? `(${option.offsetLabel}) ${option.zone}`
    : option.zone
}

/** Searchable IANA time-zone combobox with an "automatic" first entry. */
export function TimeZoneSelect({
  value,
  onChange,
  className,
}: {
  value: string | null | undefined
  onChange: (value: string) => void
  className?: string
}) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)
  const options = React.useMemo(() => buildZoneOptions(), [])
  const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const isAuto = !value || value === AUTO_VALUE

  const browserOffset = zoneOffsetLabel(browserZone)
  // ⁦…⁩ (LRI…PDI) isolates the LTR "(UTC+03:00) Zone" chunk so it
  // doesn't get bidi-mangled inside the Arabic "تلقائي — …" sentence.
  const autoLabel = t("users.timeZoneAuto", {
    zone: `⁦${browserOffset ? `(${browserOffset}) ${browserZone}` : browserZone}⁩`,
  })

  const selected = options.find((option) => option.zone === value)
  const triggerLabel = isAuto
    ? autoLabel
    : selected
      ? zoneDisplay(selected)
      : value

  const select = (next: string) => {
    onChange(next)
    setOpen(false)
  }

  return (
    <Popover open={open} onOpenChange={setOpen} modal>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className={cn("w-full justify-between font-normal", className)}
        >
          <span className="truncate" dir={isAuto ? undefined : "ltr"}>
            {triggerLabel}
          </span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      {/* Popover follows the trigger width (min 22rem) so rows never wrap. */}
      <PopoverContent
        className="w-[max(var(--radix-popover-trigger-width),22rem)] p-0"
        align="start"
      >
        <Command>
          <CommandInput placeholder={t("users.timeZoneSearch")} />
          {/* The long zone list gets an always-visible, draggable scrollbar
              (CommandList's native one is hidden by the design preset). */}
          <CommandList className="max-h-none">
            {/* The max-height lives on the Radix viewport (not the root):
                a percentage height can't resolve against a max-h-only parent,
                so the viewport would never scroll and the thumb never shows. */}
            <ScrollArea
              type="always"
              className="[&_[data-slot=scroll-area-viewport]]:max-h-72"
            >
              <CommandEmpty>{t("common.noResults")}</CommandEmpty>
              <CommandGroup>
                <CommandItem
                  value={AUTO_VALUE}
                  onSelect={() => select(AUTO_VALUE)}
                >
                  <Check
                    className={cn(
                      "shrink-0",
                      isAuto ? "opacity-100" : "opacity-0"
                    )}
                  />
                  <span className="truncate">{autoLabel}</span>
                </CommandItem>
                {options.map((option) => (
                  <CommandItem
                    key={option.zone}
                    // The offset is part of the filter value so "+03" finds zones.
                    value={zoneDisplay(option)}
                    onSelect={() => select(option.zone)}
                  >
                    <Check
                      className={cn(
                        "shrink-0",
                        value === option.zone ? "opacity-100" : "opacity-0"
                      )}
                    />
                    {option.offsetLabel ? (
                      <span
                        dir="ltr"
                        className="shrink-0 tabular-nums text-muted-foreground"
                      >
                        ({option.offsetLabel})
                      </span>
                    ) : null}
                    <span dir="ltr" className="min-w-0 truncate">
                      {option.zone}
                    </span>
                  </CommandItem>
                ))}
              </CommandGroup>
            </ScrollArea>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
