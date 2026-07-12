import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"

import {
  formatFieldValue,
  humanizeKey,
  nameSiblingKey,
  pairedLabelKey,
} from "./field-format"

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

  // Pair id-like fields with their resolved-name siblings (applicationId +
  // applicationName, createdBy + createdByName, …): the id column shows the
  // name (raw id as fallback) and the sibling is dropped so the pair doesn't
  // render twice. Only auto-discovered siblings collapse — when a curated
  // column already shows the name, the raw id column is left untouched.
  const discoveredSet = new Set(discovered)
  const consumedNameKeys = new Set<string>()
  for (const key of discovered) {
    const sibling = nameSiblingKey(key)
    if (sibling !== key && discoveredSet.has(sibling)) {
      consumedNameKeys.add(sibling)
    }
  }
  const emitted = discovered.filter((key) => !consumedNameKeys.has(key))

  const autoColumns: ColumnDef<TData, unknown>[] = emitted.map((key) => {
    const sibling = nameSiblingKey(key)
    const isPaired = consumedNameKeys.has(sibling)
    const label = humanizeKey(isPaired ? pairedLabelKey(key) : key)
    return {
      id: key,
      accessorFn: (row) => {
        const record = row as Record<string, unknown>
        if (isPaired) {
          const name = record[sibling]
          if (typeof name === "string" && name !== "") return name
        }
        return record[key]
      },
      header: label,
      meta: { label },
      cell: ({ getValue }) => (
        <span className="block max-w-[260px] truncate text-sm text-muted-foreground">
          {formatFieldValue(getValue(), t)}
        </span>
      ),
    }
  })

  const actionIndex = columns.findIndex((column) => column.id === "actions")
  const merged =
    actionIndex === -1
      ? [...columns, ...autoColumns]
      : [
          ...columns.slice(0, actionIndex),
          ...autoColumns,
          ...columns.slice(actionIndex),
        ]

  return { columns: merged, autoColumnIds: emitted }
}
