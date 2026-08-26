import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"

import { PageHeader } from "@authsystem/ui/common/page-header"
import { api } from "@authsystem/api/client"
import { toSortParams, unwrap, toNumber } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { DEFAULT_PAGE_SIZE, PERMISSIONS } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import {
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import type { AuditLogColumnId } from "./audit-log-columns"
import { AuditLogExportMenu } from "./audit-log-export"
import { AuditLogFilterRow } from "./audit-log-filter-row"
import {
  AUDIT_LOG_FILTER_SCHEMA,
  toAuditLogQuery,
  type AuditLogFilters,
} from "./audit-log-filters"
import { AuditLogTable } from "./audit-log-table"

const AUDIT_LOGS_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.auditLogs,
  defaultSorting: [{ id: "timestamp", desc: true }],
  filters: AUDIT_LOG_FILTER_SCHEMA,
} satisfies ListUrlStateOptions<AuditLogFilters>

/**
 * The application is narrowed on the server here, by the row above the table,
 * so the column repeating that value on every row starts out of the way — as it
 * did while it was auto-discovered. It stays one menu entry away, and bringing
 * it back must not bring a second application control with it: a faceted chip
 * would offer the applications on the loaded page as though they were the
 * applications, next to a select that actually re-queries.
 */
const SERVER_FILTERED_COLUMNS: readonly AuditLogColumnId[] = [
  "applicationName",
]

export function AuditLogsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const {
    pageIndex: page,
    pageSize,
    sorting,
    filters,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilters,
  } = useListUrlState(AUDIT_LOGS_LIST_URL_OPTIONS)
  const { sortBy, sortDirection } = toSortParams(sorting)

  const canExport = hasPermission(PERMISSIONS.auditLogs.export)

  // One object, spread into the list request AND the export body, so the file
  // can never hold a wider slice than the table showing it.
  const queryFilters = React.useMemo(() => toAuditLogQuery(filters), [filters])

  const query = useQuery({
    queryKey: [
      "audit-logs",
      { page, pageSize, queryFilters, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              ...queryFilters,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  const exportFilters = React.useMemo(
    () => ({ ...queryFilters, sortBy, sortDirection }),
    [queryFilters, sortBy, sortDirection]
  )

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <PageHeader
        title={t("auditLogs.title")}
        description={t("auditLogs.subtitle")}
        actions={
          canExport ? (
            <AuditLogExportMenu
              filters={exportFilters}
              totalCount={toNumber(query.data?.totalCount)}
            />
          ) : null
        }
      />

      <AuditLogFilterRow filters={filters} onChange={setFilters} />

      <AuditLogTable
        fillHeight
        tableId="audit-logs"
        defaultHidden={SERVER_FILTERED_COLUMNS}
        serverFiltered={SERVER_FILTERED_COLUMNS}
        data={query.data?.logs ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        sorting={sorting}
        onSortingChange={setSorting}
        pagination={{
          pageIndex: page,
          pageSize,
          pageCount: toNumber(query.data?.totalPages),
          totalCount: toNumber(query.data?.totalCount),
          onPageChange: setPage,
          onPageSizeChange: setPageSize,
        }}
      />
    </div>
  )
}
