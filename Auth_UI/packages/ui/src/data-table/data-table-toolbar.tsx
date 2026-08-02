import * as React from "react"
import type { Table } from "@tanstack/react-table"
import { Download, X } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { DataTableFacetedFilter } from "./data-table-faceted-filter"
import {
  DataTableViewOptions,
  getHideableColumns,
} from "./data-table-view-options"
import { Spinner } from "@authsystem/ui/spinner"

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

  const hideableColumns = getHideableColumns(table)

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
        <SearchInput
          value={(table.getState().globalFilter as string) ?? ""}
          onChange={(value) => table.setGlobalFilter(value)}
          placeholder={searchPlaceholder}
          className="w-40 lg:w-56"
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
            {isExporting ? <Spinner /> : <Download />}
            {isExporting ? t("common.exporting") : t("common.export")}
          </Button>
        ) : null}

        <DataTableViewOptions table={table} />
      </div>
    </div>
  )
}
