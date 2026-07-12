import * as React from "react"
import type { Table } from "@tanstack/react-table"
import { Download, Loader2, X } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import { DataTableFacetedFilter } from "./data-table-faceted-filter"
import { DataTableViewOptions } from "./data-table-view-options"

interface DataTableToolbarProps<TData> {
  table: Table<TData>
  /** Show a client-side global search box (off by default). */
  globalSearch?: boolean
  searchPlaceholder?: string
  /** Page-owned filters (application select, date range, …) merged into the bar. */
  extras?: React.ReactNode
  /** Render the CSV export button (on by default). */
  enableExport?: boolean
  /** Invoked when the export button is pressed. */
  onExport?: () => void
  /** Disable the export button while a fetch-all export is in flight. */
  isExporting?: boolean
  /** Disable the export button when there is nothing to export. */
  exportDisabled?: boolean
}

/**
 * Bar above the table: optional global search, an auto-generated faceted filter
 * for every column flagged `meta.filterVariant: "faceted"`, a reset action when
 * any filter is active, page extras, and the column-visibility button at the end.
 * Renders nothing when there is no control to show, so untouched tables are
 * unaffected.
 */
export function DataTableToolbar<TData>({
  table,
  globalSearch = false,
  searchPlaceholder,
  extras,
  enableExport = true,
  onExport,
  isExporting = false,
  exportDisabled = false,
}: DataTableToolbarProps<TData>) {
  const { t } = useTranslation()

  const facetedColumns = table
    .getAllColumns()
    .filter((column) => column.getCanFilter() && column.columnDef.meta?.filterVariant === "faceted")

  const hideableColumns = table
    .getAllColumns()
    .filter((column) => typeof column.accessorFn !== "undefined" && column.getCanHide())

  const isFiltered =
    table.getState().columnFilters.length > 0 ||
    Boolean(table.getState().globalFilter)

  const showExport = enableExport && Boolean(onExport)

  const hasContent =
    globalSearch ||
    facetedColumns.length > 0 ||
    hideableColumns.length > 0 ||
    showExport ||
    Boolean(extras)

  if (!hasContent) return null

  return (
    <div className="flex flex-wrap items-center gap-2">
      {globalSearch ? (
        <Input
          value={(table.getState().globalFilter as string) ?? ""}
          onChange={(event) => table.setGlobalFilter(event.target.value)}
          placeholder={searchPlaceholder ?? t("common.search")}
          className="h-8 w-40 lg:w-56"
        />
      ) : null}

      {extras}

      {facetedColumns.map((column) => (
        <DataTableFacetedFilter
          key={column.id}
          column={column}
          title={column.columnDef.meta?.label ?? column.id}
          options={column.columnDef.meta?.filterOptions}
        />
      ))}

      {isFiltered ? (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => {
            table.resetColumnFilters()
            table.setGlobalFilter("")
          }}
        >
          {t("common.reset")}
          <X />
        </Button>
      ) : null}

      <div className="ms-auto flex items-center gap-2">
        {showExport ? (
          <Button
            variant="outline"
            size="sm"
            onClick={onExport}
            disabled={isExporting || exportDisabled}
            aria-label={t("common.export")}
          >
            {isExporting ? <Loader2 className="animate-spin" /> : <Download />}
            {isExporting ? t("common.exporting") : t("common.export")}
          </Button>
        ) : null}

        <DataTableViewOptions table={table} />
      </div>
    </div>
  )
}
