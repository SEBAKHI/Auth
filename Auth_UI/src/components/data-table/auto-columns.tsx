import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"

import { formatFieldValue, humanizeKey } from "./field-format"

const SAMPLE_SIZE = 50

/**
 * Augments a page's curated columns with one hidden column per remaining field
 * found on the API response rows, so the column-visibility menu lists the full
 * record — not just the pre-selected columns. Auto columns are inserted before
 * the trailing actions column and default to hidden; callers seed that default
 * via the returned `autoColumnIds`.
 */
export function buildDisplayColumns<TData>(
  columns: ColumnDef<TData, unknown>[],
  data: TData[],
  t: TFunction
): { columns: ColumnDef<TData, unknown>[]; autoColumnIds: string[] } {
  // Fields already represented by an explicit column (by accessorKey or id).
  const covered = new Set<string>()
  for (const column of columns) {
    const accessorKey = (column as { accessorKey?: string }).accessorKey
    if (accessorKey) covered.add(accessorKey)
    if (column.id) covered.add(column.id)
  }

  // Union of field names across a sample of rows (nullable fields may be absent
  // on some rows but present on others).
  const discovered: string[] = []
  const seen = new Set<string>()
  for (const row of data.slice(0, SAMPLE_SIZE)) {
    if (!row || typeof row !== "object") continue
    for (const key of Object.keys(row)) {
      if (seen.has(key) || covered.has(key)) continue
      seen.add(key)
      discovered.push(key)
    }
  }

  if (discovered.length === 0) {
    return { columns, autoColumnIds: [] }
  }

  const autoColumns: ColumnDef<TData, unknown>[] = discovered.map((key) => ({
    id: key,
    accessorFn: (row) => (row as Record<string, unknown>)[key],
    header: humanizeKey(key),
    meta: { label: humanizeKey(key) },
    cell: ({ getValue }) => (
      <span className="block max-w-[260px] truncate text-sm text-muted-foreground">
        {formatFieldValue(getValue(), t)}
      </span>
    ),
  }))

  const actionIndex = columns.findIndex((column) => column.id === "actions")
  const merged =
    actionIndex === -1
      ? [...columns, ...autoColumns]
      : [
          ...columns.slice(0, actionIndex),
          ...autoColumns,
          ...columns.slice(actionIndex),
        ]

  return { columns: merged, autoColumnIds: discovered }
}
