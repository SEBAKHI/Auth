import * as React from "react"
import { useTranslation } from "react-i18next"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { DateRangePicker } from "@authsystem/ui/common/date-range-picker"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { SearchableSelect } from "@authsystem/ui/common/searchable-select"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { api } from "@authsystem/api/client"
import { toSortParams, unwrap, toNumber } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { AUDIT_ACTIONS, AUDIT_ACTION_TYPES } from "@/lib/audit-catalog"
import { DEFAULT_PAGE_SIZE, PERMISSIONS } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import {
  dateUrlFilter,
  stringUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { useQuery } from "@tanstack/react-query"
import { AuditLogExportMenu } from "./audit-log-export"
import { useAuditLabels } from "./audit-log-labels"
import { AuditLogTable } from "./audit-log-table"
import type { AuditLogColumnId } from "./audit-log-columns"

type AuditLogListFilters = {
  applicationId: string
  action: string
  actionType: string
  result: string
  from: string
  to: string
}

/** Sentinel for "do not narrow", since a Select item cannot carry an empty value. */
const ALL = "__all__"

const AUDIT_LOGS_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.auditLogs,
  defaultSorting: [{ id: "timestamp", desc: true }],
  filters: {
    applicationId: stringUrlFilter({ maxLength: 128 }),
    action: stringUrlFilter({ maxLength: 100 }),
    actionType: stringUrlFilter({ maxLength: 100 }),
    // A string of two values rather than booleanUrlFilter, because the choice
    // has three states and a boolean can only carry two: "" is "do not narrow",
    // which is not the same as "show me the failures". Anything else in the URL
    // canonicalizes to "", so a hand-edited link widens rather than narrows.
    result: stringUrlFilter({ pattern: /^(true|false)$/, maxLength: 5 }),
    from: dateUrlFilter(),
    to: dateUrlFilter(),
  },
} satisfies ListUrlStateOptions<AuditLogListFilters>

/**
 * The application is narrowed on the server here, by the select above the
 * table, so the column repeating that value on every row starts out of the way
 * — as it did while it was auto-discovered. It stays one menu entry away, and
 * bringing it back must not bring a second application control with it: a
 * faceted chip would offer the applications on the loaded page as though they
 * were the applications, next to a select that actually re-queries.
 */
const SERVER_FILTERED_COLUMNS: readonly AuditLogColumnId[] = [
  "applicationName",
]

function startOfDay(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString()
}
function endOfDay(date: string): string {
  return new Date(`${date}T23:59:59`).toISOString()
}

export function AuditLogsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const {
    pageIndex: page,
    pageSize,
    sorting,
    filters: {
      applicationId,
      action,
      actionType,
      result,
      from: fromDate,
      to: toDate,
    },
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilter,
    setFilters,
  } = useListUrlState(AUDIT_LOGS_LIST_URL_OPTIONS)
  const { sortBy, sortDirection } = toSortParams(sorting)

  const canExport = hasPermission(PERMISSIONS.auditLogs.export)

  // For the two selects below. What travels to the API is always the stored
  // code; the translation is only what the reader picks it out by.
  const { actionLabel, actionTypeLabel } = useAuditLabels()

  const actionOptions = React.useMemo(() => {
    const options = [
      { id: ALL, label: t("auditLogs.allActions") },
      ...AUDIT_ACTIONS.map((entry) => ({
        id: entry.code,
        label: actionLabel(entry.code),
        description: entry.code,
      })),
    ]
    // A saved link can carry a code this build has never heard of — a row from
    // before an action was retired, or a server one release ahead. Without an
    // option for it the trigger would show the placeholder while the table below
    // stays narrowed, and the reader would take a filtered page for the whole
    // table.
    if (action && !AUDIT_ACTIONS.some((entry) => entry.code === action)) {
      options.splice(1, 0, { id: action, label: action, description: action })
    }
    return options
  }, [action, actionLabel, t])

  const filters = React.useMemo(
    () => ({
      applicationId: applicationId || undefined,
      action: action || undefined,
      actionType: actionType || undefined,
      // undefined, not false, when nothing is chosen: the API matches on
      // equality, so sending false would ask for the failures.
      isSuccess: result === "" ? undefined : result === "true",
      fromDate: fromDate ? startOfDay(fromDate) : undefined,
      toDate: toDate ? endOfDay(toDate) : undefined,
    }),
    [applicationId, action, actionType, result, fromDate, toDate]
  )

  const query = useQuery({
    queryKey: [
      "audit-logs",
      { page, pageSize, filters, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              ...filters,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  // Export in the same order the table shows and under the same filters —
  // every one of them declared by the request contract, so none can be dropped
  // in silence.
  const exportFilters = React.useMemo(
    () => ({ ...filters, sortBy, sortDirection }),
    [filters, sortBy, sortDirection]
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

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        <ApplicationSelect
          value={applicationId || undefined}
          onChange={(value) => setFilter("applicationId", value ?? "")}
          allowAll
          className="w-full"
        />
        <Select
          value={actionType || ALL}
          onValueChange={(value) =>
            setFilter("actionType", value === ALL ? "" : value)
          }
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t("auditLogs.allActionTypes")}</SelectItem>
            {AUDIT_ACTION_TYPES.map((type) => (
              <SelectItem key={type} value={type}>
                {actionTypeLabel(type)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {/* Searchable rather than a plain list: forty-nine actions is past what
            a dropdown can be scanned for, and the search matches the translated
            name AND the raw code, so both ways of knowing an action work. */}
        <SearchableSelect
          value={action || ALL}
          options={actionOptions}
          onChange={(id) => setFilter("action", !id || id === ALL ? "" : id)}
          placeholder={t("auditLogs.searchAction")}
        />
        {/* Two choices, not three: the API matches the outcome on equality, so
            rows whose outcome was never recorded cannot be asked for. Offering
            "not recorded" here would be a filter that always returns nothing. */}
        <Select
          value={result || ALL}
          onValueChange={(value) =>
            setFilter("result", value === ALL ? "" : value)
          }
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t("auditLogs.allResults")}</SelectItem>
            <SelectItem value="true">{t("auditLogs.success")}</SelectItem>
            <SelectItem value="false">{t("auditLogs.failure")}</SelectItem>
          </SelectContent>
        </Select>
        <DateRangePicker
          from={fromDate || undefined}
          to={toDate || undefined}
          onChange={({ from, to }) =>
            setFilters({ from: from ?? "", to: to ?? "" })
          }
        />
      </div>

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
