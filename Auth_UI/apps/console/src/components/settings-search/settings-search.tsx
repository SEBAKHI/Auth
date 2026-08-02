import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Search } from "lucide-react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { Button } from "@authsystem/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@authsystem/ui/command"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@authsystem/ui/popover"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"

import { PERMISSIONS } from "@/lib/constants"
import { SETTINGS_QUERY_KEY } from "@/pages/system-settings/lib/sections"
import { buildSearchIndex, searchSettings } from "./build-index"

/**
 * Finds a setting by name without knowing which page it lives on.
 *
 * A panel rather than a modal: `Popover` with `modal={false}` neither traps
 * focus nor dims the page, so it reads as a dropdown from the search field —
 * which is what it is. `CommandDialog` exists in the UI package and stays
 * unused for exactly that reason.
 *
 * Lives in the console rather than the shared UI package because the results
 * are permission-filtered, which needs the auth context and the console's
 * permission constants.
 */
export function SettingsSearch() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [open, setOpen] = React.useState(false)
  const [query, setQuery] = React.useState("")
  const debounced = useDebouncedValue(query, 150)

  const canReadSettings = hasPermission(PERMISSIONS.systemSettings.manage)

  // Fetched only once the panel opens, and under the settings page's own key,
  // so opening the search warms that page rather than duplicating its request.
  const settings = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/system-settings")),
    enabled: open && canReadSettings,
  })

  const index = React.useMemo(
    // Every label in the index comes from `t`, whose identity changes on a
    // language switch — so the index rebuilds in the new language on its own.
    () => buildSearchIndex(settings.data?.sections ?? [], t, hasPermission),
    [settings.data, t, hasPermission]
  )

  const results = React.useMemo(
    () => searchSettings(index, debounced),
    [index, debounced]
  )

  const go = (route: string) => {
    setOpen(false)
    setQuery("")
    void navigate(route)
  }

  // ⌘K / Ctrl+K from anywhere, so the search does not depend on reaching for
  // the header first.
  React.useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== "k" || !(event.metaKey || event.ctrlKey)) {
        return
      }
      event.preventDefault()
      setOpen((previous) => !previous)
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [])

  const hasResults =
    results.surfaces.length > 0 || results.fieldGroups.length > 0

  return (
    <Popover open={open} onOpenChange={setOpen} modal={false}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          aria-label={t("settingsSearch.label")}
          className="w-48 justify-start text-muted-foreground lg:w-64"
        >
          <Search data-icon="inline-start" />
          <span className="truncate">{t("settingsSearch.placeholder")}</span>
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="end"
        sideOffset={8}
        className="w-[min(42rem,calc(100vw-2rem))] p-0"
      >
        {/* Scoring is two-tier and also matches raw config paths, neither of
            which cmdk's built-in filter can express. */}
        <Command shouldFilter={false}>
          <CommandInput
            value={query}
            onValueChange={setQuery}
            placeholder={t("settingsSearch.placeholder")}
          />
          <CommandList className="max-h-[60vh]">
            {debounced && !hasResults ? (
              <CommandEmpty>{t("settingsSearch.noResults")}</CommandEmpty>
            ) : null}

            {!debounced ? (
              <CommandEmpty>{t("settingsSearch.hint")}</CommandEmpty>
            ) : null}

            {results.surfaces.length > 0 ? (
              <CommandGroup heading={t("settingsSearch.pages")}>
                {results.surfaces.map((entry) => (
                  <CommandItem
                    key={entry.id}
                    value={entry.id}
                    onSelect={() => go(entry.route)}
                  >
                    <div className="flex flex-col gap-0.5">
                      <span className="font-medium">{entry.title}</span>
                      {entry.description ? (
                        <span className="line-clamp-1 text-xs text-muted-foreground">
                          {entry.description}
                        </span>
                      ) : null}
                    </div>
                  </CommandItem>
                ))}
              </CommandGroup>
            ) : null}

            {results.surfaces.length > 0 && results.fieldGroups.length > 0 ? (
              <CommandSeparator />
            ) : null}

            {/* One group per section, and the rows inside are visibly smaller
                and indented — a single option is not the same kind of result
                as a whole page. */}
            {results.fieldGroups.map((group) => (
              <CommandGroup key={group.sectionId} heading={group.sectionTitle}>
                {group.fields.map((field) => (
                  <CommandItem
                    key={field.id}
                    value={field.id}
                    onSelect={() => go(field.route)}
                    className="ps-6"
                  >
                    <div className="flex flex-col gap-0.5">
                      <span className="text-sm">{field.title}</span>
                      {field.description ? (
                        <span className="line-clamp-1 text-xs text-muted-foreground">
                          {field.description}
                        </span>
                      ) : null}
                    </div>
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
