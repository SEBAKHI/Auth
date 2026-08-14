import * as React from "react"
import { Check, ChevronsUpDown } from "lucide-react"
import { useTranslation } from "react-i18next"
import { useQuery } from "@tanstack/react-query"

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
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { fullName } from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import { cn } from "@authsystem/ui/utils"

/**
 * Searchable user picker.
 *
 * Searches server-side rather than loading every user: the directory can be
 * large, and a picker that silently shows only the first page is a picker that
 * cannot find most people.
 */
export function UserSelect({
  id,
  value,
  onChange,
  excludeIds,
  placeholder,
  className,
}: {
  /** Trigger id, so a `FieldLabel htmlFor` can name the control. */
  id?: string
  value: string | undefined
  /**
   * Receives the id and the display label. Callers that stage a selection for
   * later need the label too — the id alone leaves them showing a raw GUID,
   * and re-deriving it means refetching a user they just picked.
   */
  onChange: (value: string | undefined, label?: string) => void
  /** Users already assigned; hidden from the list. */
  excludeIds?: Set<string>
  placeholder?: string
  className?: string
}) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)

  const query = useQuery({
    queryKey: ["users", "picker", search],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users", {
          params: {
            query: {
              pageNumber: 1,
              pageSize: 20,
              // The endpoint's parameter is `searchTerm`. Sending `search`
              // silently returned the unfiltered first page, so typing a name
              // that matched nobody still listed everybody.
              searchTerm: search || undefined,
            },
          },
        })
      ),
  })

  const users = (query.data?.users ?? []).filter(
    (user) => user.id && !excludeIds?.has(user.id)
  )
  const selected = users.find((user) => user.id === value)

  const label = (user: (typeof users)[number]) => {
    const name = fullName(user.firstName, user.lastName) || user.displayName
    return name ? `${name} — ${user.email}` : (user.email ?? "")
  }

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
            {selected
              ? label(selected)
              : (placeholder ?? t("common.search"))}
          </span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[max(var(--radix-popover-trigger-width),22rem)] p-0"
        align="start"
      >
        {/* Filtering happens on the server, so the client-side matcher is off:
            leaving it on would hide results the search deliberately returned. */}
        <Command shouldFilter={false}>
          <CommandInput
            placeholder={placeholder ?? t("common.search")}
            value={searchInput}
            onValueChange={setSearchInput}
          />
          <CommandList className="max-h-none">
            <ScrollArea
              type="always"
              className="[&_[data-slot=scroll-area-viewport]]:max-h-72"
            >
              <CommandEmpty>
                {query.isLoading ? t("common.loading") : t("common.noResults")}
              </CommandEmpty>
              <CommandGroup>
                {users.map((user) => (
                  <CommandItem
                    key={user.id}
                    value={user.id as string}
                    onSelect={() => {
                      onChange(user.id as string, label(user))
                      setOpen(false)
                    }}
                  >
                    <Check
                      className={cn(
                        "shrink-0",
                        value === user.id ? "opacity-100" : "opacity-0"
                      )}
                    />
                    <span className="min-w-0 truncate">{label(user)}</span>
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
