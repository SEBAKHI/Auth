import * as React from "react"
import { flushSync } from "react-dom"
import {
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type ColumnFiltersState,
  type ColumnSizingState,
  type Header,
  type OnChangeFn,
  type SortingState,
  type VisibilityState,
} from "@tanstack/react-table"
import { ArrowDown, ArrowUp, ChevronLeft, ChevronRight, ChevronsUpDown } from "lucide-react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { Button } from "@astoom/ui/button"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import { cn } from "@astoom/ui/utils"
import { getErrorMessage } from "@astoom/api/errors"
import { directionForLanguage } from "@astoom/i18n"
import { buildDisplayColumns } from "./auto-columns"
import { buildExportColumns, exportRowsToCsv } from "./csv"
import { DataTableRowDetail } from "./data-table-row-detail"
import { humanizeKey } from "./field-format"
import { facetedFilterFn } from "./filters"
import { DataTableToolbar } from "./data-table-toolbar"
import "./types"

const PAGE_SIZES = [10, 20, 50, 100]

export interface DataTablePagination {
  pageIndex: number
  pageSize: number
  pageCount: number
  totalCount?: number
  onPageChange: (pageIndex: number) => void
  onPageSizeChange: (pageSize: number) => void
}

interface DataTableProps<TData> {
  columns: ColumnDef<TData, unknown>[]
  data: TData[]
  isLoading?: boolean
  error?: unknown
  onRetry?: () => void
  emptyMessage?: string
  /** Provide for server-paginated tables; omit for in-place arrays. */
  pagination?: DataTablePagination
  /**
   * Stable key used to persist column-visibility choices in localStorage.
   * Omit to keep visibility in-memory only.
   */
  tableId?: string
  /** Render the toolbar (search/filters/columns button). Defaults to true. */
  enableToolbar?: boolean
  /** Show a client-side global search box in the toolbar. */
  globalSearch?: boolean
  searchPlaceholder?: string
  /** Page-owned filter controls to merge into the toolbar. */
  toolbarExtras?: React.ReactNode
  /** Render the CSV export button (defaults to true). */
  enableExport?: boolean
  /** Base name for the exported file; defaults to `tableId` or "export". */
  exportFileName?: string
  /**
   * Fetch the full, filter-aware dataset for export. Omit on in-memory tables —
   * export then uses the loaded `data`. Provide on server-paginated tables so the
   * export covers every page, not just the current one.
   */
  onExportAll?: () => Promise<TData[]>
  /** Enable click-to-open row detail panel (defaults to true). */
  enableRowDetail?: boolean
  /** Show an Edit button in the detail panel that hands the row to the page. */
  onEditRow?: (row: TData) => void
  /** Field names grouped under "Audit Fields" in the detail panel. */
  auditFieldKeys?: readonly string[]
  /** Build the detail panel title from the open row. */
  getDetailTitle?: (row: TData) => string
  /**
   * Server-side sorting: pass both `sorting` and `onSortingChange` to lift the
   * sort state to the page (which forwards it as `sortBy`/`sortDirection` API
   * params). The table then stops sorting the loaded page locally, so the
   * clicked header orders the entire dataset, not just the visible rows.
   * Omit both on fully-loaded tables — local sorting already covers all rows.
   */
  sorting?: SortingState
  onSortingChange?: (sorting: SortingState) => void
}

function readPersistedVisibility(tableId?: string): VisibilityState {
  if (!tableId || typeof window === "undefined") return {}
  try {
    const raw = window.localStorage.getItem(`dt:cols:${tableId}`)
    return raw ? (JSON.parse(raw) as VisibilityState) : {}
  } catch {
    return {}
  }
}

function readPersistedSizing(tableId?: string): ColumnSizingState {
  if (!tableId || typeof window === "undefined") return {}
  try {
    const raw = window.localStorage.getItem(`dt:size:${tableId}`)
    return raw ? (JSON.parse(raw) as ColumnSizingState) : {}
  } catch {
    return {}
  }
}

/**
 * Header cell that toggles sorting; used only for columns that can sort. The
 * visible column title stays the accessible name; sort state is announced via
 * `aria-sort` on the parent `<th>`.
 */
function SortableHeader<TData>({ header }: { header: Header<TData, unknown> }) {
  const sorted = header.column.getIsSorted()
  return (
    <Button
      variant="ghost"
      size="sm"
      className="-ms-2.5 h-8"
      onClick={header.column.getToggleSortingHandler()}
    >
      <span>{flexRender(header.column.columnDef.header, header.getContext())}</span>
      {sorted === "asc" ? (
        <ArrowUp />
      ) : sorted === "desc" ? (
        <ArrowDown />
      ) : (
        <ChevronsUpDown className="opacity-50" />
      )}
    </Button>
  )
}

/**
 * Data table built on TanStack Table + shadcn primitives. Provides, for every
 * table that uses it, client-side sorting, faceted filtering, and a
 * column-visibility menu (rendered via the toolbar), plus loading, error, and
 * empty states. Server pagination stays opt-in via `pagination`; sorting and
 * filtering then operate on the loaded page only.
 */
export function DataTable<TData>({
  columns,
  data,
  isLoading = false,
  error,
  onRetry,
  emptyMessage,
  pagination,
  tableId,
  enableToolbar = true,
  globalSearch = false,
  searchPlaceholder,
  toolbarExtras,
  enableExport = true,
  exportFileName,
  onExportAll,
  enableRowDetail = true,
  onEditRow,
  auditFieldKeys,
  getDetailTitle,
  sorting: controlledSorting,
  onSortingChange: onControlledSortingChange,
}: DataTableProps<TData>) {
  const { t, i18n } = useTranslation()

  const [internalSorting, setInternalSorting] = React.useState<SortingState>([])
  const [columnFilters, setColumnFilters] = React.useState<ColumnFiltersState>([])
  const [globalFilter, setGlobalFilter] = React.useState("")
  const [columnVisibility, setColumnVisibility] = React.useState<VisibilityState>(
    () => readPersistedVisibility(tableId)
  )
  const [columnSizing, setColumnSizing] = React.useState<ColumnSizingState>(
    () => readPersistedSizing(tableId)
  )
  const [detailRow, setDetailRow] = React.useState<TData | null>(null)
  const [isExporting, setIsExporting] = React.useState(false)

  React.useEffect(() => {
    if (!tableId || typeof window === "undefined") return
    try {
      window.localStorage.setItem(`dt:cols:${tableId}`, JSON.stringify(columnVisibility))
    } catch {
      // Ignore storage failures (private mode / quota); visibility stays in-memory.
    }
  }, [tableId, columnVisibility])

  React.useEffect(() => {
    if (!tableId || typeof window === "undefined") return
    try {
      window.localStorage.setItem(`dt:size:${tableId}`, JSON.stringify(columnSizing))
    } catch {
      // Ignore storage failures (private mode / quota); sizing stays in-memory.
    }
  }, [tableId, columnSizing])

  // Server-controlled sorting is active when the page lifts the sort state.
  const isManualSorting =
    controlledSorting !== undefined && Boolean(onControlledSortingChange)
  const sorting = isManualSorting ? (controlledSorting as SortingState) : internalSorting
  const handleSortingChange: OnChangeFn<SortingState> = (updater) => {
    const next = typeof updater === "function" ? updater(sorting) : updater
    if (isManualSorting) onControlledSortingChange?.(next)
    else setInternalSorting(next)
  }

  // Augment the page's curated columns with one hidden column per remaining
  // field on the data rows, so the visibility menu lists the full record.
  const built = React.useMemo(
    () => buildDisplayColumns(columns, data, t),
    [columns, data, t]
  )
  // Under server sorting, auto-discovered columns must not offer sorting — their
  // field names are not in the endpoint's sortBy allow-list (the API would 400).
  // The trailing actions column keeps its natural width, so it never resizes.
  const effectiveColumns = React.useMemo(() => {
    const autoIds = new Set<string>(built.autoColumnIds)
    return built.columns.map((column) => {
      const disableSorting =
        isManualSorting && Boolean(column.id && autoIds.has(column.id))
      const disableResizing = column.id === "actions"
      if (!disableSorting && !disableResizing) return column
      return {
        ...column,
        ...(disableSorting ? { enableSorting: false } : {}),
        ...(disableResizing ? { enableResizing: false } : {}),
      }
    })
  }, [built, isManualSorting])
  const autoHiddenDefaults = React.useMemo(() => {
    const defaults: VisibilityState = {}
    for (const id of built.autoColumnIds) defaults[id] = false
    return defaults
  }, [built])
  // Merge the hidden-by-default auto columns at render time (rather than via an
  // effect) so newly discovered fields stay hidden until the user opts in,
  // while persisted/user choices still win.
  const effectiveVisibility = React.useMemo(
    () => ({ ...autoHiddenDefaults, ...columnVisibility }),
    [autoHiddenDefaults, columnVisibility]
  )

  const table = useReactTable({
    data,
    columns: effectiveColumns,
    filterFns: { faceted: facetedFilterFn },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
    manualSorting: isManualSorting,
    onSortingChange: handleSortingChange,
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: (updater) =>
      setColumnVisibility((prev) => {
        const current = { ...autoHiddenDefaults, ...prev }
        return typeof updater === "function" ? updater(current) : updater
      }),
    onGlobalFilterChange: setGlobalFilter,
    enableColumnResizing: true,
    columnResizeMode: "onChange",
    // The same function `DirectionProvider` writes onto `documentElement.dir`, so
    // the drag maths and the layout cannot disagree. `i18n.dir()` can: it reads
    // i18next's `resolvedLanguage`, which is not the active language on a cold
    // load (see `initI18n`). An `ltr` value here against an RTL table flips
    // TanStack's delta sign and the column resizes away from the cursor.
    columnResizeDirection: directionForLanguage(i18n.language),
    defaultColumn: { minSize: 60 },
    onColumnSizingChange: setColumnSizing,
    manualPagination: Boolean(pagination),
    pageCount: pagination ? Math.max(pagination.pageCount, 1) : undefined,
    state: {
      sorting,
      columnFilters,
      globalFilter,
      columnVisibility: effectiveVisibility,
      columnSizing,
      ...(pagination
        ? {
            pagination: {
              pageIndex: pagination.pageIndex,
              pageSize: pagination.pageSize,
            },
          }
        : {}),
    },
  })

  const visibleColumnCount = table.getVisibleLeafColumns().length
  const rows = table.getRowModel().rows

  // TanStack measures a drag against the column's current size, which is the
  // built-in default for columns that were never resized (their widths are
  // content-driven until then). Seed the real rendered width into the sizing
  // state synchronously so the drag starts from what the user actually sees.
  const beginResize = React.useCallback(
    (
      event: React.MouseEvent | React.TouchEvent,
      header: Header<TData, unknown>
    ) => {
      event.stopPropagation()
      const headCell = (event.currentTarget as HTMLElement).closest("th")
      const columnId = header.column.id
      if (headCell && columnSizing[columnId] == null) {
        const width = Math.round(headCell.getBoundingClientRect().width)
        flushSync(() => {
          setColumnSizing((prev) => ({ ...prev, [columnId]: width }))
        })
      }
      header.getResizeHandler()(event)
    },
    [columnSizing]
  )

  // Field name → localized label, sourced from the same column definitions the
  // grid uses, so the detail panel reuses the page's translated headers.
  const detailLabelMap = React.useMemo(() => {
    const map: Record<string, string> = {}
    for (const column of table.getAllLeafColumns()) {
      if (column.id === "actions") continue
      const def = column.columnDef as { accessorKey?: string; header?: unknown }
      const key = def.accessorKey ?? column.id
      const label =
        column.columnDef.meta?.label ??
        (typeof def.header === "string" ? def.header : humanizeKey(column.id))
      map[key] = label
    }
    return map
    // built.columns drives the leaf set; recompute when columns or locale change.
  }, [built, t]) // eslint-disable-line react-hooks/exhaustive-deps

  const detailHiddenKeys = React.useMemo(
    () =>
      table
        .getAllLeafColumns()
        .filter((column) => column.columnDef.meta?.detailHidden)
        .map((column) => {
          const def = column.columnDef as { accessorKey?: string }
          return def.accessorKey ?? column.id
        }),
    [built] // eslint-disable-line react-hooks/exhaustive-deps
  )

  const exportDisabled = data.length === 0 && !onExportAll

  const handleExport = React.useCallback(async () => {
    const exportColumns = buildExportColumns(table)
    const fileBase = exportFileName ?? tableId ?? "export"
    if (!onExportAll) {
      exportRowsToCsv(data, exportColumns, fileBase, t)
      return
    }
    setIsExporting(true)
    try {
      const all = await onExportAll()
      exportRowsToCsv(all, exportColumns, fileBase, t)
    } catch (err) {
      toast.error(getErrorMessage(err, t("common.error")))
    } finally {
      setIsExporting(false)
    }
  }, [table, exportFileName, tableId, onExportAll, data, t])

  return (
    <div className="flex flex-col gap-3">
      {enableToolbar ? (
        <DataTableToolbar
          table={table}
          globalSearch={globalSearch}
          searchPlaceholder={searchPlaceholder}
          extras={toolbarExtras}
          enableExport={enableExport}
          onExport={enableExport ? handleExport : undefined}
          isExporting={isExporting}
          exportDisabled={exportDisabled}
        />
      ) : null}

      <div className="overflow-hidden rounded-lg border">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {group.headers.map((header) => {
                  // Only user-resized columns get an explicit width; the rest
                  // keep the content-driven auto layout.
                  const resizedWidth =
                    columnSizing[header.column.id] != null
                      ? header.getSize()
                      : undefined
                  return (
                    <TableHead
                      key={header.id}
                      className="relative"
                      style={
                        resizedWidth != null
                          ? {
                              width: resizedWidth,
                              minWidth: resizedWidth,
                              maxWidth: resizedWidth,
                            }
                          : undefined
                      }
                      aria-sort={
                        header.column.getCanSort()
                          ? header.column.getIsSorted() === "asc"
                            ? "ascending"
                            : header.column.getIsSorted() === "desc"
                              ? "descending"
                              : "none"
                          : undefined
                      }
                    >
                      {header.isPlaceholder ? null : header.column.getCanSort() ? (
                        <SortableHeader header={header} />
                      ) : (
                        flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )
                      )}
                      {header.column.getCanResize() ? (
                        <div
                          role="separator"
                          aria-orientation="vertical"
                          onMouseDown={(event) => beginResize(event, header)}
                          onTouchStart={(event) => beginResize(event, header)}
                          onDoubleClick={() => header.column.resetSize()}
                          className={cn(
                            "absolute inset-y-0 end-0 z-10 w-1.5 cursor-col-resize touch-none select-none",
                            header.column.getIsResizing()
                              ? "bg-primary/50"
                              : "hover:bg-border"
                          )}
                        />
                      ) : null}
                    </TableHead>
                  )
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading ? (
              Array.from({ length: 6 }).map((_, rowIdx) => (
                <TableRow key={`skeleton-${rowIdx}`}>
                  {Array.from({ length: visibleColumnCount }).map((__, cellIdx) => (
                    <TableCell key={`skeleton-${rowIdx}-${cellIdx}`}>
                      <Skeleton className="h-5 w-full" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={visibleColumnCount} className="h-32 text-center">
                  <div className="flex flex-col items-center gap-2">
                    <p className="text-sm text-muted-foreground">
                      {getErrorMessage(error, t("common.error"))}
                    </p>
                    {onRetry ? (
                      <Button variant="outline" size="sm" onClick={onRetry}>
                        {t("common.retry")}
                      </Button>
                    ) : null}
                  </div>
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={visibleColumnCount}
                  className="h-32 text-center text-sm text-muted-foreground"
                >
                  {emptyMessage ?? t("common.noResults")}
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row) => (
                <TableRow
                  key={row.id}
                  {...(enableRowDetail
                    ? {
                        // Keep the native row semantics (a11y/grid) but make the
                        // row focusable and openable; the trailing actions cell
                        // stops propagation so its menu keeps working.
                        tabIndex: 0,
                        "aria-label": t("common.details"),
                        className: "cursor-pointer",
                        onClick: () => setDetailRow(row.original),
                        onKeyDown: (event: React.KeyboardEvent) => {
                          // Only when the row itself is focused, so an inner
                          // button keeps its own keyboard behavior.
                          if (event.target !== event.currentTarget) return
                          if (event.key === "Enter" || event.key === " ") {
                            event.preventDefault()
                            setDetailRow(row.original)
                          }
                        },
                      }
                    : {})}
                >
                  {row.getVisibleCells().map((cell) => {
                    const resizedWidth =
                      columnSizing[cell.column.id] != null
                        ? cell.column.getSize()
                        : undefined
                    return (
                      <TableCell
                        key={cell.id}
                        // Cells are whitespace-nowrap; the explicit max width
                        // is what lets a narrowed column truncate instead of
                        // forcing the table wider.
                        className={
                          resizedWidth != null
                            ? "overflow-hidden text-ellipsis"
                            : undefined
                        }
                        style={
                          resizedWidth != null
                            ? {
                                width: resizedWidth,
                                minWidth: resizedWidth,
                                maxWidth: resizedWidth,
                              }
                            : undefined
                        }
                        {...(enableRowDetail && cell.column.id === "actions"
                          ? {
                              onClick: (event: React.MouseEvent) =>
                                event.stopPropagation(),
                            }
                          : {})}
                      >
                        {flexRender(
                          cell.column.columnDef.cell,
                          cell.getContext()
                        )}
                      </TableCell>
                    )
                  })}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {pagination ? (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-sm text-muted-foreground">
            {typeof pagination.totalCount === "number"
              ? t("common.showing", {
                  count: data.length,
                  total: pagination.totalCount,
                })
              : null}
          </p>
          <div className="flex items-center gap-2">
            <Select
              value={String(pagination.pageSize)}
              onValueChange={(value) =>
                pagination.onPageSizeChange(Number(value))
              }
            >
              <SelectTrigger size="sm" className="w-[120px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {PAGE_SIZES.map((size) => (
                    <SelectItem key={size} value={String(size)}>
                      {size} / {t("common.page")}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
            <span className="px-1 text-sm text-muted-foreground">
              {t("common.pageOf", {
                page: pagination.pageIndex + 1,
                total: Math.max(pagination.pageCount, 1),
              })}
            </span>
            <Button
              variant="outline"
              size="icon-sm"
              aria-label={t("common.previous")}
              disabled={pagination.pageIndex <= 0 || isLoading}
              onClick={() => pagination.onPageChange(pagination.pageIndex - 1)}
            >
              <ChevronLeft className="rtl:rotate-180" />
            </Button>
            <Button
              variant="outline"
              size="icon-sm"
              aria-label={t("common.next")}
              disabled={
                pagination.pageIndex + 1 >= pagination.pageCount || isLoading
              }
              onClick={() => pagination.onPageChange(pagination.pageIndex + 1)}
            >
              <ChevronRight className="rtl:rotate-180" />
            </Button>
          </div>
        </div>
      ) : null}

      {enableRowDetail ? (
        <DataTableRowDetail
          row={detailRow}
          open={detailRow !== null}
          onOpenChange={(open) => {
            if (!open) setDetailRow(null)
          }}
          labelMap={detailLabelMap}
          auditFieldKeys={auditFieldKeys}
          hiddenKeys={detailHiddenKeys}
          onEdit={onEditRow}
          title={
            detailRow && getDetailTitle ? getDetailTitle(detailRow) : undefined
          }
        />
      ) : null}
    </div>
  )
}
