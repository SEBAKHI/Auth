import { Check, ChevronsUpDown } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@authsystem/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@authsystem/ui/popover"
import { ScrollArea } from "@authsystem/ui/scroll-area"
import { cn } from "@authsystem/ui/utils"

/**
 * The label, isolated as a left-to-right run when asked. `bdi` is the whole
 * mechanism: it stops the surrounding direction from reaching into the run, and
 * stops the run from disturbing the line around it — without moving either.
 */
function Label({ value, ltr }: { value: string; ltr: boolean }) {
  return ltr ? <bdi dir="ltr">{value}</bdi> : <>{value}</>
}

export interface SearchableOption {
  id?: string
  /** The line the reader scans for, and the primary search target. */
  label?: string
  /** A second line under it, also searchable. */
  description?: string
}

/**
 * A searchable single-choice picker over a bounded, already-loaded list.
 *
 * One component for permissions and roles rather than one each: the two
 * assignment dialogs differ in what they list and in nothing else, and the
 * first version of this was written twice before that was obvious.
 *
 * Filtered in the browser, unlike UserSelect, which searches server-side
 * because the user directory is unbounded. These catalogues are small and the
 * callers already hold them in full, so a round-trip per keystroke would buy
 * nothing.
 */
export function SearchableSelect({
  id,
  value,
  options,
  onChange,
  placeholder,
  className,
  ltrLabel = false,
}: {
  id?: string
  value: string | undefined
  options: SearchableOption[]
  onChange: (id: string | undefined) => void
  placeholder?: string
  className?: string
  /**
   * Isolates the label as a left-to-right run, for content that is always Latin
   * — permission codes above all.
   *
   * Isolation only. ALIGNMENT still follows the page, so the list reads down a
   * straight edge on the same side as every other list in the interface. An
   * earlier version set `dir="ltr"` on the whole option and got both at once:
   * the codes came out in the right order and the column jumped to the other
   * side of the popover, which is not what "fix the direction" asked for.
   *
   * What isolation buys on its own: a code ending in a neutral character, like
   * `org:members:*`, has that character resolved to the paragraph direction by
   * the bidirectional algorithm and is painted as `*:org:members` — the same
   * characters in an order that reads as a different permission.
   */
  ltrLabel?: boolean
}) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)

  const selected = options.find((option) => option.id === value)

  return (
    <Popover open={open} onOpenChange={setOpen} modal>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className={cn("w-full justify-between font-normal", className)}
        >
          <span className="truncate">
            {selected?.label ? (
              <Label value={selected.label} ltr={ltrLabel} />
            ) : (
              (placeholder ?? t("common.search"))
            )}
          </span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[max(var(--radix-popover-trigger-width),22rem)] p-0"
        align="start"
      >
        <Command>
          <CommandInput placeholder={placeholder ?? t("common.search")} />
          <CommandList className="max-h-none">
            <ScrollArea
              type="always"
              className="[&_[data-slot=scroll-area-viewport]]:max-h-72"
            >
              <CommandEmpty>{t("common.noResults")}</CommandEmpty>
              <CommandGroup>
                {options.map((option) => (
                  <CommandItem
                    key={option.id}
                    // Both lines go into the searchable value: cmdk matches on
                    // this string, and the label alone would leave the
                    // description unsearchable — so an operator hunting for
                    // "delete users" could not find users:delete by name.
                    value={`${option.label ?? ""} ${option.description ?? ""}`}
                    onSelect={() => {
                      onChange(option.id)
                      setOpen(false)
                    }}
                  >
                    <Check
                      className={cn(
                        "shrink-0",
                        value === option.id ? "opacity-100" : "opacity-0"
                      )}
                    />
                    <span className="flex min-w-0 flex-col text-start">
                      <span className="truncate font-medium">
                        <Label value={option.label ?? ""} ltr={ltrLabel} />
                      </span>
                      {option.description ? (
                        <span className="truncate text-xs text-muted-foreground">
                          {option.description}
                        </span>
                      ) : null}
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
