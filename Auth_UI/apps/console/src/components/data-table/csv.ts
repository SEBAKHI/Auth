import type { Table } from "@tanstack/react-table"
import type { TFunction } from "i18next"

import { formatFieldValue, humanizeKey } from "./field-format"

/** Byte-order mark (U+FEFF) so Excel reads UTF-8 (and Arabic) output correctly. */
const BOM = String.fromCharCode(0xfeff)

/** A single exported column: its human header and a value accessor. */
export interface ExportColumn {
  /** Localized header text. */
  label: string
  /** Resolve the cell value for a raw row (uses the column's own accessor). */
  getValue: (row: unknown, index: number) => unknown
}

/**
 * Derive the CSV columns from what the user currently sees: the **visible** leaf
 * columns, in display order, with the same labels shown in the header. Each
 * column's own `accessorFn` resolves the value (so composite columns export
 * their displayed value). Excludes the trailing actions column, pure display
 * columns (no accessor), and anything opted out via `meta.excludeFromExport`.
 */
export function buildExportColumns<TData>(table: Table<TData>): ExportColumn[] {
  const columns: ExportColumn[] = []
  for (const column of table.getVisibleLeafColumns()) {
    if (column.id === "actions") continue
    if (column.columnDef.meta?.excludeFromExport) continue
    // Skip display-only columns that carry no value (no accessorFn/accessorKey).
    const accessor = column.accessorFn
    if (typeof accessor === "undefined") continue
    columns.push({
      label: column.columnDef.meta?.label ?? humanizeKey(column.id),
      getValue: (row, index) => accessor(row as TData, index),
    })
  }
  return columns
}

const NEEDS_QUOTING = /["\n\r,]/
const FORMULA_TRIGGER = /^[=+\-@\t\r]/

/** Quote/escape a single cell, neutralizing spreadsheet formula injection. */
export function escapeCsvCell(raw: string): string {
  // Guard against CSV/formula injection in spreadsheet apps.
  const guarded = FORMULA_TRIGGER.test(raw) ? `'${raw}` : raw
  if (NEEDS_QUOTING.test(guarded) || guarded !== raw) {
    return `"${guarded.replace(/"/g, '""')}"`
  }
  return guarded
}

/** Serialize rows + columns into a CSV string (CRLF line endings, no BOM). */
export function rowsToCsvString(
  rows: unknown[],
  columns: ExportColumn[],
  t: TFunction
): string {
  const header = columns.map((column) => escapeCsvCell(column.label)).join(",")
  const lines = rows.map((row, index) =>
    columns
      .map((column) =>
        escapeCsvCell(formatFieldValue(column.getValue(row, index), t))
      )
      .join(",")
  )
  return [header, ...lines].join("\r\n")
}

/** Trigger a browser download of `content` as a UTF-8 (BOM) `.csv` file. */
export function downloadCsv(content: string, fileName: string): void {
  if (typeof document === "undefined") return
  const blob = new Blob([BOM + content], {
    type: "text/csv;charset=utf-8;",
  })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement("a")
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}

/** Date stamp `YYYY-MM-DD` appended to exported file names. */
function dateStamp(): string {
  return new Date().toISOString().slice(0, 10)
}

/**
 * High-level helper: serialize `rows` against `columns` and download the file as
 * `${baseName}-${YYYY-MM-DD}.csv`.
 */
export function exportRowsToCsv(
  rows: unknown[],
  columns: ExportColumn[],
  baseName: string,
  t: TFunction
): void {
  const content = rowsToCsvString(rows, columns, t)
  downloadCsv(content, `${baseName}-${dateStamp()}.csv`)
}
