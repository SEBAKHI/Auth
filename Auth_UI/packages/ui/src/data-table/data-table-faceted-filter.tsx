import * as React from "react"
import type { Column } from "@tanstack/react-table"
import { PlusCircle } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Checkbox } from "@authsystem/ui/checkbox"
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
import { Separator } from "@authsystem/ui/separator"

interface FacetOption {
  label: string
  value: string
}

interface DataTableFacetedFilterProps<TData, TValue> {
  column: Column<TData, TValue>
  title?: string
  options?: FacetOption[]
}

/**
 * Toolbar multi-select filter for a column with a small set of discrete values
 * (status, environment, …). Options come from `meta.filterOptions` when given,
 * otherwise they are derived from the column's faceted unique values. The
 * column must use `filterFn: "faceted"`.
 */
export function DataTableFacetedFilter<TData, TValue>({
  column,
  title,
  options,
}: DataTableFacetedFilterProps<TData, TValue>) {
  const { t } = useTranslation()
  const rawFacets = column.getFacetedUniqueValues()

  // Faceted counts are keyed by the raw accessor value; normalise to strings so
  // they match string option values (e.g. boolean `isSystem`).
  const facets = React.useMemo(() => {
    const map = new Map<string, number>()
    for (const [value, count] of rawFacets) {
      const key = String(value)
      map.set(key, (map.get(key) ?? 0) + count)
    }
    return map
  }, [rawFacets])

  const resolvedOptions = React.useMemo<FacetOption[]>(() => {
    if (options && options.length > 0) return options
    return Array.from(facets.keys())
      .filter((value) => value !== "" && value !== "null" && value !== "undefined")
      .sort()
      .map((value) => ({ label: value, value }))
  }, [options, facets])

  const selectedValues = new Set((column.getFilterValue() as string[]) ?? [])

  if (resolvedOptions.length === 0) return null

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="border-dashed">
          <PlusCircle data-icon="inline-start" />
          {title}
          {selectedValues.size > 0 ? (
            <>
              <Separator orientation="vertical" className="mx-2 h-4" />
              <Badge variant="secondary" className="lg:hidden">
                {selectedValues.size}
              </Badge>
              <div className="hidden gap-1 lg:flex">
                {selectedValues.size > 2 ? (
                  <Badge variant="secondary">
                    {t("common.selectedCount", { count: selectedValues.size })}
                  </Badge>
                ) : (
                  resolvedOptions
                    .filter((option) => selectedValues.has(option.value))
                    .map((option) => (
                      <Badge variant="secondary" key={option.value}>
                        {option.label}
                      </Badge>
                    ))
                )}
              </div>
            </>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-52 p-0" align="start">
        <Command>
          <CommandInput placeholder={title} />
          {/* A handful of options in a narrow popover; a scrollbar here would
              be more chrome than the content. */}
          <CommandList className="no-scrollbar">
            <CommandEmpty>{t("common.noResults")}</CommandEmpty>
            <CommandGroup>
              {resolvedOptions.map((option) => {
                const isSelected = selectedValues.has(option.value)
                return (
                  <CommandItem
                    key={option.value}
                    // cmdk owns `aria-selected` for the keyboard highlight, so
                    // the multi-select state rides on `aria-checked` — without
                    // it the tick is a purely visual cue.
                    aria-checked={isSelected}
                    onSelect={() => {
                      if (isSelected) selectedValues.delete(option.value)
                      else selectedValues.add(option.value)
                      const filterValues = Array.from(selectedValues)
                      column.setFilterValue(
                        filterValues.length ? filterValues : undefined
                      )
                    }}
                  >
                    {/* Presentational: the row itself is the control, and a
                        real checkbox nested inside it would be a second tab
                        stop announcing the same thing twice. */}
                    <Checkbox
                      checked={isSelected}
                      tabIndex={-1}
                      aria-hidden
                      className="pointer-events-none"
                    />
                    <span>{option.label}</span>
                    {facets.get(option.value) ? (
                      <span className="ms-auto flex size-4 items-center justify-center font-mono text-xs text-muted-foreground">
                        {facets.get(option.value)}
                      </span>
                    ) : null}
                  </CommandItem>
                )
              })}
            </CommandGroup>
            {selectedValues.size > 0 ? (
              <>
                <CommandSeparator />
                <CommandGroup>
                  <CommandItem
                    onSelect={() => column.setFilterValue(undefined)}
                    className="justify-center text-center"
                  >
                    {t("common.clear")}
                  </CommandItem>
                </CommandGroup>
              </>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
