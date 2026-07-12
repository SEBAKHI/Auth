import type { FilterFn } from "@tanstack/react-table"

/**
 * Multi-select (faceted) filter: keeps a row when its column value matches one
 * of the selected values. Registered once on the table as the `"faceted"`
 * filter function so columns can opt in with `filterFn: "faceted"`.
 */
export const facetedFilterFn: FilterFn<unknown> = (row, columnId, filterValue) => {
  const selected = filterValue as string[] | undefined
  if (!selected || selected.length === 0) return true
  return selected.includes(String(row.getValue(columnId)))
}
