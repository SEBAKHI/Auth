import * as React from "react"
import type { Column } from "@tanstack/react-table"
import { Check, PlusCircle } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from "@astoom/ui/command"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@astoom/ui/popover"
import { Separator } from "@astoom/ui/separator"
import { cn } from "@astoom/ui/utils"

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
          <PlusCircle />
          {title}
          {selectedValues.size > 0 ? (
            <>
              <Separator orientation="vertical" className="mx-2 h-4" />
              <Badge variant="secondary" className="rounded-sm px-1 font-normal lg:hidden">
                {selectedValues.size}
              </Badge>
              <div className="hidden gap-1 lg:flex">
                {selectedValues.size > 2 ? (
                  <Badge variant="secondary" className="rounded-sm px-1 font-normal">
                    {t("common.selectedCount", { count: selectedValues.size })}
                  </Badge>
                ) : (
                  resolvedOptions
                    .filter((option) => selectedValues.has(option.value))
                    .map((option) => (
                      <Badge
                        variant="secondary"
                        key={option.value}
                        className="rounded-sm px-1 font-normal"
                      >
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
          <CommandList>
            <CommandEmpty>{t("common.noResults")}</CommandEmpty>
            <CommandGroup>
              {resolvedOptions.map((option) => {
                const isSelected = selectedValues.has(option.value)
                return (
                  <CommandItem
                    key={option.value}
                    onSelect={() => {
                      if (isSelected) selectedValues.delete(option.value)
                      else selectedValues.add(option.value)
                      const filterValues = Array.from(selectedValues)
                      column.setFilterValue(
                        filterValues.length ? filterValues : undefined
                      )
                    }}
                  >
                    <div
                      className={cn(
                        "flex size-4 items-center justify-center rounded-md border border-primary",
                        isSelected
                          ? "bg-primary text-primary-foreground"
                          : "opacity-50 [&_svg]:invisible"
                      )}
                    >
                      <Check className="size-3.5" />
                    </div>
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
