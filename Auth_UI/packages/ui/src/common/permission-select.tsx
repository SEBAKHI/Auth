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
import { PermissionCode } from "@authsystem/ui/common/permission-code"
import { Popover, PopoverContent, PopoverTrigger } from "@authsystem/ui/popover"
import { ScrollArea } from "@authsystem/ui/scroll-area"
import { cn } from "@authsystem/ui/utils"

export interface PermissionOption {
  id?: string
  code?: string
  name?: string
}

/**
 * Searchable permission picker.
 *
 * Filtered in the browser rather than on the server, unlike UserSelect: the
 * permission catalogue is a bounded list already loaded in full by the callers,
 * so a round-trip per keystroke would buy nothing.
 *
 * It replaced a plain Select. Once every enforced code has a row the list runs
 * past fifty entries, and a Select offers no way to reach one except scrolling
 * past the rest. The search matches the NAME as well as the code, because an
 * operator looking for "delete users" should not have to know it is spelled
 * users:delete.
 */
export function PermissionSelect({
  id,
  value,
  options,
  onChange,
  placeholder,
  className,
}: {
  id?: string
  value: string | undefined
  options: PermissionOption[]
  onChange: (id: string | undefined) => void
  placeholder?: string
  className?: string
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
            {selected?.code ? (
              <PermissionCode code={selected.code} />
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
                    // Both fields go in the searchable value: cmdk matches
                    // against this string, and the code alone would make the
                    // human-readable name unsearchable.
                    value={`${option.code ?? ""} ${option.name ?? ""}`}
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
                    <span className="flex min-w-0 flex-col">
                      <PermissionCode
                        code={option.code ?? ""}
                        className="truncate font-medium"
                      />
                      {option.name ? (
                        <span className="truncate text-xs text-muted-foreground">
                          {option.name}
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
