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
import {
  ArrowDown,
  ArrowUp,
  ChevronLeft,
  ChevronRight,
  ChevronsUpDown,
  Inbox,
  TriangleAlert,
} from "lucide-react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { Button } from "@authsystem/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { Skeleton } from "@authsystem/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@authsystem/ui/table"
import { cn } from "@authsystem/ui/utils"
import { getErrorMessage } from "@authsystem/api/errors"
import { directionForLanguage } from "@authsystem/i18n"
import { buildDisplayColumns } from "./auto-columns"
import {
  ACTIONS_COLUMN_ID,
  columnPosition,
  moveColumn,
  reorderColumn,
  resolveColumnOrder,
} from "./column-order"
import { buildExportColumns, exportRowsToCsv } from "./csv"
import {
  readTableLayout,
  subscribeTableLayout,
  writeTableLayout,
  type TableLayout,
} from "./storage"
import { DataTableRowDetail } from "./data-table-row-detail"
import { fieldLabel } from "./field-format"
import { facetedFilterFn } from "./filters"
import { DataTableToolbar } from "./data-table-toolbar"
import "./types"

const PAGE_SIZES = [10, 20, 50, 100]

/**
 * Column-width bounds and keyboard resize steps, in pixels. The maximum keeps a
 * dragged column from pushing every other one out of the viewport; the steps
 * mirror the usual grid convention of a fine nudge plus a coarse one on Shift.
 */
const MIN_COLUMN_WIDTH = 60
const MAX_COLUMN_WIDTH = 800
const RESIZE_STEP = 8
const RESIZE_STEP_LARGE = 32

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
  /**
   * Term the search box starts with. Set by a page that was opened with a
   * query already in hand — the command palette handing off the rest of its
   * matches — so the table arrives filtered rather than showing everything and
   * making the user type it a second time.
   */
  initialGlobalFilter?: string
  /** Controlled global search for URL-owned client-side list state. */
  globalFilter?: string
  onGlobalFilterChange?: (value: string) => void
  /**
   * Column filters the table opens with. Seeded the same way as
   * `initialGlobalFilter`: a page arriving from a deep link ("show me the keys
   * that are about to expire") lands on those rows instead of on everything,
   * and the reader can still clear the filter without the URL re-applying it.
   */
  initialColumnFilters?: ColumnFiltersState
  /**
   * Controlled filters for URL-owned list state. Pass both props together;
   * omit them to retain the table's existing in-memory behavior.
   */
  columnFilters?: ColumnFiltersState
  onColumnFiltersChange?: (filters: ColumnFiltersState) => void
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
  /**
   * Fill the available height and scroll the rows inside the table instead of
   * scrolling the page. Keeps the page title, the toolbar and the pagination
   * bar on screen no matter how many rows load, and pins the column headers.
   *
   * The page must give the table something to fill — a `h-full` root — or
   * there is no available height to take.
   */
  fillHeight?: boolean
}

function readPersistedOrder(layout: TableLayout): string[] {
  // A hand-edited or half-written value must not take the grid down.
  return Array.isArray(layout.order)
    ? layout.order.filter((id): id is string => typeof id === "string")
    : []
}

/** The id TanStack will derive for a column definition. */
function columnIdOf(column: ColumnDef<unknown, unknown>): string {
  const def = column as { id?: string; accessorKey?: string }
  return def.id ?? def.accessorKey ?? ""
}

/**
 * Drops entries that match what the table would do anyway, so the persisted
 * blob stays proportional to the user's actual choices. Without it every toggle
 * rewrites the whole auto-discovered set, and the stored object gains an entry
 * for every field the API ever adds to its response — permanently.
 *
 * An entry is only dropped when its default is actually known. Auto-discovered
 * columns do not exist until a row has arrived, so before the first fetch every
 * such id would otherwise look like "visible, same as the default" and the
 * user's choice to show it would be pruned away and written back as a loss.
 */
function pruneVisibility(
  visibility: VisibilityState,
  defaults: VisibilityState,
  knownIds: ReadonlySet<string>
): VisibilityState {
  const pruned: VisibilityState = {}
  for (const [id, visible] of Object.entries(visibility)) {
    if (!knownIds.has(id) || visible !== (defaults[id] ?? true)) {
      pruned[id] = visible
    }
  }
  return pruned
}

/** Stable shape for change detection, so equal layouts serialize identically. */
function serializeLayout(layout: TableLayout): string {
  return JSON.stringify({
    cols: layout.cols ?? {},
    size: layout.size ?? {},
    order: layout.order ?? [],
  })
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
      <span>
        {flexRender(header.column.columnDef.header, header.getContext())}
      </span>
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
/**
 * Anything in a row that answers a click itself. The row's own click opens the
 * detail panel, so it has to stand aside for these - links above all, since a
 * link that also opened an overlay would be two answers to one click.
 */
const INTERACTIVE_IN_ROW =
  "a[href], button, input, select, textarea, label, [role='button'], [role='link'], [role='menuitem'], [role='checkbox'], [role='switch'], [contenteditable='true']"

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
  initialGlobalFilter = "",
  initialColumnFilters = [],
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
  columnFilters: controlledColumnFilters,
  onColumnFiltersChange: onControlledColumnFiltersChange,
  globalFilter: controlledGlobalFilter,
  onGlobalFilterChange: onControlledGlobalFilterChange,
  fillHeight = false,
}: DataTableProps<TData>) {
  "use no memo"

  const { t, i18n } = useTranslation()
  const direction = directionForLanguage(i18n.language)
  const isRtl = direction === "rtl"

  const [internalSorting, setInternalSorting] = React.useState<SortingState>([])
  // Seeded exactly like the global filter below, so a page can open pre-filtered
  // from a deep link and the table owns it from there — clearing the filter then
  // does not fight the URL that set it.
  const [internalColumnFilters, setInternalColumnFilters] =
    React.useState<ColumnFiltersState>(initialColumnFilters)
  // Seeded, not controlled: the caller says what the table opens with and the
  // table owns it from there, so typing does not have to round-trip through
  // the page that mounted it.
  const [internalGlobalFilter, setInternalGlobalFilter] =
    React.useState(initialGlobalFilter)
  // One stored document per table, read synchronously so the first paint is
  // already the user's layout rather than the default rearranging itself.
  const [initialLayout] = React.useState<TableLayout>(() =>
    readTableLayout(tableId)
  )
  const [columnVisibility, setColumnVisibility] =
    React.useState<VisibilityState>(() => initialLayout.cols ?? {})
  const [columnSizing, setColumnSizing] = React.useState<ColumnSizingState>(
    () => initialLayout.size ?? {}
  )
  const [columnOrder, setColumnOrder] = React.useState<string[]>(() =>
    readPersistedOrder(initialLayout)
  )
  // What the store already holds, so a render that changes nothing does not
  // rewrite it — and does not push an identical document to the server.
  const lastWrittenRef = React.useRef(
    serializeLayout({
      cols: initialLayout.cols ?? {},
      size: initialLayout.size ?? {},
      order: readPersistedOrder(initialLayout),
    })
  )
  const [draggedColumnId, setDraggedColumnId] = React.useState<string | null>(
    null
  )
  // Reordering has no visual feedback a screen reader can use, so every move is
  // narrated here. This is the keyboard path's only confirmation.
  const [orderAnnouncement, setOrderAnnouncement] = React.useState("")
  const [detailRow, setDetailRow] = React.useState<TData | null>(null)
  const [isExporting, setIsExporting] = React.useState(false)

  // Adopt a layout that arrived from elsewhere: the server copy landing after
  // first paint, or a different user signing in on this browser.
  React.useEffect(() => {
    if (!tableId) return
    return subscribeTableLayout(tableId, () => {
      const layout = readTableLayout(tableId)
      const order = readPersistedOrder(layout)
      lastWrittenRef.current = serializeLayout({
        cols: layout.cols ?? {},
        size: layout.size ?? {},
        order,
      })
      setColumnVisibility(layout.cols ?? {})
      setColumnSizing(layout.size ?? {})
      setColumnOrder(order)
    })
  }, [tableId])

  const hasControlledSorting =
    controlledSorting !== undefined && Boolean(onControlledSortingChange)
  // Paginated tables delegate sorting to the endpoint. Fully-loaded tables can
  // lift sorting into the URL while TanStack still orders their local rows.
  const isManualSorting = hasControlledSorting && Boolean(pagination)
  const sorting = hasControlledSorting
    ? (controlledSorting as SortingState)
    : internalSorting
  const handleSortingChange: OnChangeFn<SortingState> = (updater) => {
    const next = typeof updater === "function" ? updater(sorting) : updater
    if (hasControlledSorting) onControlledSortingChange?.(next)
    else setInternalSorting(next)
  }
  const hasControlledColumnFilters =
    controlledColumnFilters !== undefined &&
    Boolean(onControlledColumnFiltersChange)
  const columnFilters = hasControlledColumnFilters
    ? (controlledColumnFilters as ColumnFiltersState)
    : internalColumnFilters
  const handleColumnFiltersChange: OnChangeFn<ColumnFiltersState> = (
    updater
  ) => {
    const next =
      typeof updater === "function" ? updater(columnFilters) : updater
    if (hasControlledColumnFilters) onControlledColumnFiltersChange?.(next)
    else setInternalColumnFilters(next)
  }
  const hasControlledGlobalFilter =
    controlledGlobalFilter !== undefined &&
    Boolean(onControlledGlobalFilterChange)
  const globalFilter = hasControlledGlobalFilter
    ? controlledGlobalFilter
    : internalGlobalFilter
  const handleGlobalFilterChange: OnChangeFn<string> = (updater) => {
    const next = typeof updater === "function" ? updater(globalFilter) : updater
    if (hasControlledGlobalFilter) onControlledGlobalFilterChange?.(next)
    else setInternalGlobalFilter(next)
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

  // The stored order is reconciled against the columns that exist right now:
  // auto-discovery changes the set with the API payload, so it is always
  // partial. See `resolveColumnOrder` for the invariants this upholds.
  const naturalColumnIds = React.useMemo(
    () =>
      (effectiveColumns as ColumnDef<unknown, unknown>[])
        .map(columnIdOf)
        .filter(Boolean),
    [effectiveColumns]
  )
  const effectiveOrder = React.useMemo(
    () => resolveColumnOrder(naturalColumnIds, columnOrder),
    [naturalColumnIds, columnOrder]
  )

  const knownColumnIds = React.useMemo(
    () => new Set(naturalColumnIds),
    [naturalColumnIds]
  )

  // Persisted after `autoHiddenDefaults` and the column set exist, so only
  // genuine departures from the default are written (see `pruneVisibility`).
  React.useEffect(() => {
    if (!tableId) return
    const next: TableLayout = {
      cols: pruneVisibility(
        columnVisibility,
        autoHiddenDefaults,
        knownColumnIds
      ),
      size: columnSizing,
      order: columnOrder,
    }
    const serialized = serializeLayout(next)
    if (serialized === lastWrittenRef.current) return
    lastWrittenRef.current = serialized
    writeTableLayout(tableId, next)
  }, [
    tableId,
    columnVisibility,
    autoHiddenDefaults,
    knownColumnIds,
    columnSizing,
    columnOrder,
  ])

  // TanStack Table intentionally returns a mutable facade. React Compiler
  // already skips this component (`use no memo` above); this boundary is the
  // library's documented contract, not an application value crossing into a
  // memoized child.
  // eslint-disable-next-line react-hooks/incompatible-library -- compiler interop boundary
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
    onColumnFiltersChange: handleColumnFiltersChange,
    onColumnVisibilityChange: (updater) =>
      setColumnVisibility((prev) => {
        const current = { ...autoHiddenDefaults, ...prev }
        return typeof updater === "function" ? updater(current) : updater
      }),
    onGlobalFilterChange: handleGlobalFilterChange,
    onColumnOrderChange: (updater) =>
      setColumnOrder(
        typeof updater === "function" ? updater(effectiveOrder) : updater
      ),
    enableColumnResizing: true,
    columnResizeMode: "onChange",
    // The same function `DirectionProvider` writes onto `documentElement.dir`, so
    // the drag maths and the layout cannot disagree. `i18n.dir()` can: it reads
    // i18next's `resolvedLanguage`, which is not the active language on a cold
    // load (see `initI18n`). An `ltr` value here against an RTL table flips
    // TanStack's delta sign and the column resizes away from the cursor.
    columnResizeDirection: direction,
    defaultColumn: { minSize: MIN_COLUMN_WIDTH, maxSize: MAX_COLUMN_WIDTH },
    onColumnSizingChange: setColumnSizing,
    manualPagination: Boolean(pagination),
    pageCount: pagination ? Math.max(pagination.pageCount, 1) : undefined,
    state: {
      sorting,
      columnFilters,
      globalFilter,
      columnVisibility: effectiveVisibility,
      columnSizing,
      columnOrder: effectiveOrder,
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

  // Auto-discovered columns only exist once a row has arrived, so the skeleton
  // would otherwise render fewer columns than the loaded grid and the layout
  // would jump on every refetch. Remember the last real width instead.
  const lastColumnCountRef = React.useRef(visibleColumnCount)
  React.useEffect(() => {
    if (!isLoading && visibleColumnCount > 0) {
      lastColumnCountRef.current = visibleColumnCount
    }
  }, [isLoading, visibleColumnCount])
  const skeletonColumnCount = Math.max(
    visibleColumnCount,
    lastColumnCountRef.current
  )

  // TanStack measures a resize against the column's current size, which is the
  // built-in default for columns that were never resized (their widths are
  // content-driven until then). Seed the real rendered width into the sizing
  // state synchronously so the gesture starts from what the user actually sees,
  // and so `aria-valuenow` reports a real number rather than that default.
  const seedWidth = React.useCallback(
    (element: HTMLElement | null, columnId: string): number | undefined => {
      const existing = columnSizing[columnId]
      if (existing != null) return existing
      const headCell = element?.closest("th")
      if (!headCell) return undefined
      const width = Math.round(headCell.getBoundingClientRect().width)
      flushSync(() => {
        setColumnSizing((prev) => ({ ...prev, [columnId]: width }))
      })
      return width
    },
    [columnSizing]
  )

  const columnLabel = React.useCallback(
    (columnId: string) => {
      const match = (effectiveColumns as ColumnDef<unknown, unknown>[]).find(
        (column) => columnIdOf(column) === columnId
      )
      return match?.meta?.label ?? fieldLabel(columnId, t)
    },
    [effectiveColumns, t]
  )

  // Single funnel for both reorder paths (menu buttons and drag), so the move
  // and its announcement can never drift apart.
  const applyOrder = React.useCallback(
    (nextOrder: string[], movedId: string) => {
      // The helpers return the input untouched when the move is a no-op.
      if (nextOrder === effectiveOrder) return
      setColumnOrder(nextOrder)
      const { position, total } = columnPosition(nextOrder, movedId)
      setOrderAnnouncement(
        t("common.columnMoved", {
          column: columnLabel(movedId),
          position,
          total,
        })
      )
    },
    [effectiveOrder, columnLabel, t]
  )

  const handleMoveColumn = React.useCallback(
    (columnId: string, delta: number) =>
      applyOrder(moveColumn(effectiveOrder, columnId, delta), columnId),
    [applyOrder, effectiveOrder]
  )

  const beginResize = React.useCallback(
    (
      event: React.MouseEvent | React.TouchEvent,
      header: Header<TData, unknown>
    ) => {
      event.stopPropagation()
      seedWidth(event.currentTarget as HTMLElement, header.column.id)
      header.getResizeHandler()(event)
    },
    [seedWidth]
  )

  // The pointer gesture has a keyboard equivalent: a focusable separator that
  // does not respond to arrow keys is unreachable for anyone not using a mouse.
  const resizeByKeyboard = React.useCallback(
    (event: React.KeyboardEvent, header: Header<TData, unknown>) => {
      if (event.key === "Home") {
        event.preventDefault()
        header.column.resetSize()
        return
      }
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return
      event.preventDefault()

      const current = seedWidth(
        event.currentTarget as HTMLElement,
        header.column.id
      )
      if (current == null) return

      // The handle sits at the column's inline end, so the key that widens is
      // the one pointing away from the column: Right in LTR, Left in RTL.
      const widens = isRtl
        ? event.key === "ArrowLeft"
        : event.key === "ArrowRight"
      const step = event.shiftKey ? RESIZE_STEP_LARGE : RESIZE_STEP
      const min = header.column.columnDef.minSize ?? MIN_COLUMN_WIDTH
      const max = header.column.columnDef.maxSize ?? MAX_COLUMN_WIDTH
      const next = Math.min(
        max,
        Math.max(min, current + (widens ? step : -step))
      )
      setColumnSizing((prev) => ({ ...prev, [header.column.id]: next }))
    },
    [isRtl, seedWidth]
  )

  // Field name → localized label, sourced from the same column definitions the
  // grid uses, so the detail panel reuses the page's translated headers. Derived
  // from `effectiveColumns` rather than `table.getAllLeafColumns()`: the table
  // instance is rebuilt every render and so cannot be a dependency, which is
  // what the silenced exhaustive-deps warning used to hide.
  const detailLabelMap = React.useMemo(() => {
    const map: Record<string, string> = {}
    for (const column of effectiveColumns) {
      const def = column as {
        id?: string
        accessorKey?: string
        header?: unknown
      }
      const key = def.accessorKey ?? def.id
      if (!key || key === "actions") continue
      map[key] =
        column.meta?.label ??
        (typeof def.header === "string" ? def.header : fieldLabel(key, t))
    }
    return map
  }, [effectiveColumns, t])

  const detailHiddenKeys = React.useMemo(
    () =>
      effectiveColumns
        .filter((column) => column.meta?.detailHidden)
        .map((column) => {
          const def = column as { id?: string; accessorKey?: string }
          return def.accessorKey ?? def.id ?? ""
        })
        .filter(Boolean),
    [effectiveColumns]
  )

  const exportDisabled = data.length === 0 && !onExportAll

  const handleExport = React.useCallback(async () => {
    const exportColumns = buildExportColumns(table, t)
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
    <div
      className={cn(
        "flex flex-col gap-3",
        // Takes the height the page hands it, so only the rows scroll.
        fillHeight && "min-h-0 flex-1"
      )}
    >
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
          onMoveColumn={handleMoveColumn}
        />
      ) : null}

      {/* Outside the menu, which unmounts on close and would take the
          announcement with it before a screen reader read it. */}
      <div aria-live="polite" className="sr-only">
        {orderAnnouncement}
      </div>

      <div
        className={cn(
          "rounded-lg border",
          fillHeight
            ? // The 160px floor belongs HERE, on the clipping wrapper, not on
              // the scroller inside it. With `min-h-0` here and `min-h-40`
              // there, a short viewport squeezed this wrapper to 96px while the
              // scroller still believed it was 160px tall: the bottom 64px of
              // its viewport sat below the clip, and the last row could not be
              // scrolled into sight at all - at 320x568 it ended at 160px
              // inside a 96px box. Holding the floor out here makes `main`
              // scroll instead, and every row stays reachable.
              "flex min-h-40 flex-1 flex-col overflow-hidden"
            : "overflow-hidden"
        )}
      >
        <Table
          containerClassName={
            // The container is the scrolling element, so it owns the height.
            fillHeight ? "min-h-0 flex-1 overflow-auto" : undefined
          }
        >
          <TableHeader
            className={
              // Pinned while the rows scroll under it. Opaque, or the rows
              // show through; `top-0` is relative to the scroll container.
              fillHeight ? "sticky top-0 z-20 bg-background" : undefined
            }
          >
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {group.headers.map((header) => {
                  // Only user-resized columns get an explicit width; the rest
                  // keep the content-driven auto layout.
                  const resizedWidth =
                    columnSizing[header.column.id] != null
                      ? header.getSize()
                      : undefined
                  const canReorder =
                    header.column.id !== ACTIONS_COLUMN_ID &&
                    header.column.columnDef.meta?.enableReordering !== false
                  const isDropTarget =
                    canReorder &&
                    draggedColumnId !== null &&
                    draggedColumnId !== header.column.id
                  return (
                    <TableHead
                      key={header.id}
                      // The whole cell is the drop target so the aim is
                      // forgiving; only the inner block is the drag source, so
                      // the resize handle keeps its own gesture.
                      onDragOver={(event) => {
                        if (isDropTarget) event.preventDefault()
                      }}
                      onDrop={(event) => {
                        if (!isDropTarget || !draggedColumnId) return
                        event.preventDefault()
                        applyOrder(
                          reorderColumn(
                            effectiveOrder,
                            draggedColumnId,
                            header.column.id
                          ),
                          draggedColumnId
                        )
                        setDraggedColumnId(null)
                      }}
                      className={cn("relative", isDropTarget && "bg-muted/50")}
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
                      <div
                        draggable={canReorder}
                        onDragStart={(event) => {
                          event.dataTransfer.effectAllowed = "move"
                          setDraggedColumnId(header.column.id)
                        }}
                        onDragEnd={() => setDraggedColumnId(null)}
                        className={cn(
                          "flex items-center",
                          canReorder && "cursor-grab active:cursor-grabbing",
                          draggedColumnId === header.column.id && "opacity-50"
                        )}
                      >
                        {header.isPlaceholder ? null : header.column.getCanSort() ? (
                          <SortableHeader header={header} />
                        ) : (
                          flexRender(
                            header.column.columnDef.header,
                            header.getContext()
                          )
                        )}
                      </div>
                      {header.column.getCanResize() ? (
                        <div
                          role="separator"
                          aria-orientation="vertical"
                          // Focusable and arrow-driven: a separator that only
                          // answers to a pointer is invisible to keyboard use.
                          tabIndex={0}
                          aria-label={t("common.resizeColumn", {
                            column:
                              header.column.columnDef.meta?.label ??
                              fieldLabel(header.column.id, t),
                          })}
                          aria-valuenow={
                            columnSizing[header.column.id] ?? header.getSize()
                          }
                          aria-valuemin={
                            header.column.columnDef.minSize ?? MIN_COLUMN_WIDTH
                          }
                          aria-valuemax={
                            header.column.columnDef.maxSize ?? MAX_COLUMN_WIDTH
                          }
                          onMouseDown={(event) => beginResize(event, header)}
                          onTouchStart={(event) => beginResize(event, header)}
                          onDoubleClick={() => header.column.resetSize()}
                          onFocus={(event) =>
                            seedWidth(event.currentTarget, header.column.id)
                          }
                          onKeyDown={(event) => resizeByKeyboard(event, header)}
                          className={cn(
                            "absolute inset-y-0 end-0 z-10 w-1.5 cursor-col-resize touch-none outline-none select-none",
                            header.column.getIsResizing()
                              ? "bg-primary/50"
                              : "hover:bg-border focus-visible:bg-primary"
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
                  {Array.from({ length: skeletonColumnCount }).map(
                    (__, cellIdx) => (
                      <TableCell key={`skeleton-${rowIdx}-${cellIdx}`}>
                        <Skeleton className="h-5 w-full" />
                      </TableCell>
                    )
                  )}
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={visibleColumnCount}>
                  <Empty>
                    <EmptyHeader>
                      <EmptyMedia variant="icon">
                        <TriangleAlert />
                      </EmptyMedia>
                      <EmptyTitle>{t("common.error")}</EmptyTitle>
                      <EmptyDescription>
                        {getErrorMessage(error, t("common.error"))}
                      </EmptyDescription>
                    </EmptyHeader>
                    {onRetry ? (
                      <EmptyContent>
                        <Button variant="outline" size="sm" onClick={onRetry}>
                          {t("common.retry")}
                        </Button>
                      </EmptyContent>
                    ) : null}
                  </Empty>
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={visibleColumnCount}>
                  <Empty>
                    <EmptyHeader>
                      <EmptyMedia variant="icon">
                        <Inbox />
                      </EmptyMedia>
                      <EmptyTitle>
                        {emptyMessage ?? t("common.noResults")}
                      </EmptyTitle>
                    </EmptyHeader>
                  </Empty>
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
                        onClick: (event: React.MouseEvent) => {
                          // A control inside the row owns its own click. A
                          // record name is a link, so clicking it must navigate
                          // and nothing else; opening this panel at the same
                          // time would replace the page the person just asked
                          // for with an overlay describing the row they left.
                          if (
                            (event.target as HTMLElement).closest(
                              INTERACTIVE_IN_ROW
                            )
                          ) {
                            return
                          }
                          setDetailRow(row.original)
                        },
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
                          resizedWidth != null ? "truncate" : undefined
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
              <SelectTrigger
                size="sm"
                className="w-[120px]"
                aria-label={t("common.rowsPerPage")}
              >
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
