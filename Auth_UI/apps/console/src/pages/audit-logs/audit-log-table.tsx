import type { ColumnFiltersState, SortingState } from "@tanstack/react-table"
import * as React from "react"

import {
  DataTable,
  type DataTablePagination,
} from "@authsystem/ui/data-table/data-table"
import type { Schemas } from "@authsystem/api/types"

import { AuditLogDetailDialog } from "./audit-log-detail-dialog"
import { useAuditLogColumns, type AuditLogColumnId } from "./audit-log-columns"

type AuditLogDto = Schemas["AuditLogDto"]

export interface AuditLogTableProps {
  /**
   * Persistence key for this surface's column layout, and it must differ from
   * every other audit table's. Shared columns are not shared widths: the full
   * page is laid out edge to edge and a tab panel is not, so one stored
   * document would re-apply each one's pixel widths to the other — and the
   * layout is synced per user to the server, so it would follow them to every
   * device with no way back.
   */
  tableId: string
  data: AuditLogDto[]
  isLoading?: boolean
  error?: unknown
  onRetry?: () => void
  /** Ids to start hidden here. Pass a module-scope constant, not a literal. */
  defaultHidden?: readonly AuditLogColumnId[]
  /** Ids this surface narrows on the server, so they offer no filter chip. */
  serverFiltered?: readonly AuditLogColumnId[]
  sorting: SortingState
  onSortingChange: (sorting: SortingState) => void
  pagination: DataTablePagination
  /**
   * Forwarded only when the caller owns them. The shared table infers
   * controlled mode from the pair being present, so handing it an empty value
   * with a handler that discards would make every facet click revert on the
   * next render.
   */
  columnFilters?: ColumnFiltersState
  onColumnFiltersChange?: (filters: ColumnFiltersState) => void
  /** Requires a height chain above the table; a tab panel has none. */
  fillHeight?: boolean
}

/**
 * The audit table, wherever it is read.
 *
 * Deliberately thin. It owns the three things that would otherwise be written
 * out once per surface — the column set, the detail dialog and the state that
 * opens it — and passes everything else straight through. What it does NOT own
 * is the query, the URL state or the page-level filters: those differ per
 * surface for real reasons (one is scoped to a user, the other is not), and a
 * component that modelled both would be a worse `DataTable`.
 *
 * The generic row-detail sheet stays off. Both surfaces open the same dedicated
 * dialog from the Eye button instead, because it is the one that renders the
 * three-state outcome and the old/new JSON, and one kind of row should not have
 * two different detail views.
 */
export function AuditLogTable({
  tableId,
  data,
  isLoading,
  error,
  onRetry,
  defaultHidden,
  serverFiltered,
  sorting,
  onSortingChange,
  pagination,
  columnFilters,
  onColumnFiltersChange,
  fillHeight,
}: AuditLogTableProps) {
  const [detail, setDetail] = React.useState<AuditLogDto | undefined>()
  const columns = useAuditLogColumns({
    defaultHidden,
    serverFiltered,
    onViewDetail: setDetail,
  })

  return (
    <>
      <DataTable
        tableId={tableId}
        columns={columns}
        data={data}
        isLoading={isLoading}
        error={error}
        onRetry={onRetry}
        fillHeight={fillHeight}
        // Audit logs keep their dedicated server-side export (CSV/JSON, in the
        // page header) and the JSON-diff detail dialog (Eye action). The
        // table's own CSV would write the loaded page only, behind a button
        // that looks identical to the one that writes every matching row.
        enableExport={false}
        enableRowDetail={false}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        sorting={sorting}
        onSortingChange={onSortingChange}
        pagination={pagination}
      />

      {detail ? (
        <AuditLogDetailDialog
          open={Boolean(detail)}
          onOpenChange={(open) => !open && setDetail(undefined)}
          log={detail}
        />
      ) : null}
    </>
  )
}
