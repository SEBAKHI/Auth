import { useMutation, useQuery } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Download, Eye } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { DateRangePicker } from "@authsystem/ui/common/date-range-picker"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { SearchableSelect } from "@authsystem/ui/common/searchable-select"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
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
import {
  AUDIT_ACTIONS,
  AUDIT_ACTION_TYPES,
  auditActionI18nKey,
  auditActionTypeI18nKey,
} from "@/lib/audit-catalog"
import { DEFAULT_PAGE_SIZE, PERMISSIONS } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime } from "@authsystem/ui/format"
import {
  dateUrlFilter,
  stringUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import type { Schemas } from "@authsystem/api/types"
import { AuditLogDetailDialog } from "./audit-log-detail-dialog"

type AuditLogDto = Schemas["AuditLogDto"]

type AuditLogListFilters = {
  applicationId: string
  action: string
  actionType: string
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
    from: dateUrlFilter(),
    to: dateUrlFilter(),
  },
} satisfies ListUrlStateOptions<AuditLogListFilters>

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
    filters: { applicationId, action, actionType, from: fromDate, to: toDate },
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilter,
    setFilters,
  } = useListUrlState(AUDIT_LOGS_LIST_URL_OPTIONS)
  const [detail, setDetail] = React.useState<AuditLogDto | undefined>()
  const { sortBy, sortDirection } = toSortParams(sorting)

  const canExport = hasPermission(PERMISSIONS.auditLogs.export)

  // The stored code is what is filtered, exported and copied into a ticket; the
  // translation is only what it is READ as. Both are shown, and only the code
  // ever leaves this component.
  const actionLabel = React.useCallback(
    (code: string) =>
      t(`auditLogs.actions.${auditActionI18nKey(code)}`, {
        defaultValue: code,
      }),
    [t]
  )
  const actionTypeLabel = React.useCallback(
    (type: string) =>
      t(`auditLogs.actionTypes.${auditActionTypeI18nKey(type)}`, {
        defaultValue: type,
      }),
    [t]
  )

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
      fromDate: fromDate ? startOfDay(fromDate) : undefined,
      toDate: toDate ? endOfDay(toDate) : undefined,
    }),
    [applicationId, action, actionType, fromDate, toDate]
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

  const exportMutation = useMutation({
    mutationFn: async (format: "csv" | "json") => {
      const { data, error } = await api.POST("/api/v1/audit-logs/export", {
        // Export in the same order the table currently shows, and under the same
        // filters — every one of them declared by the request contract, so none
        // can be dropped in silence.
        body: { format, ...filters, maxRecords: 10000, sortBy, sortDirection },
        parseAs: "blob",
      })
      if (error) throw error
      return { blob: data as unknown as Blob, format }
    },
    onSuccess: ({ blob, format }) => {
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement("a")
      anchor.href = url
      anchor.download = `audit-logs.${format}`
      anchor.click()
      URL.revokeObjectURL(url)
      toast.success(t("auditLogs.exported"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<AuditLogDto, unknown>[] = [
    {
      id: "action",
      accessorFn: (row) => row.action ?? "",
      header: t("auditLogs.action"),
      meta: { label: t("auditLogs.action") },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">
            {actionLabel(row.original.action ?? "")}
          </p>
          {/* The stored value, kept in view: it is what a support ticket, a URL
              filter and a SQL query all need, and it is the same string in every
              language. */}
          <p className="truncate text-xs text-muted-foreground">
            <bdi dir="auto">{row.original.action}</bdi>
          </p>
        </div>
      ),
    },
    {
      id: "actionType",
      accessorFn: (row) => row.actionType ?? "",
      // Not sortable: SortFields.AuditLogs has no actionType, and the API
      // rejects an unlisted sort field with a 400 rather than ignoring it.
      enableSorting: false,
      header: t("auditLogs.actionType"),
      meta: { label: t("auditLogs.actionType") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.actionType
            ? actionTypeLabel(row.original.actionType)
            : "—"}
        </span>
      ),
    },
    {
      id: "entityType",
      accessorFn: (row) => row.entityType ?? "",
      filterFn: "faceted",
      header: t("auditLogs.target"),
      meta: { label: t("auditLogs.target"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.entityType ?? "—"}
        </span>
      ),
    },
    {
      id: "actor",
      accessorFn: (row) => row.userEmail ?? row.userName ?? "",
      header: t("auditLogs.actor"),
      meta: { label: t("auditLogs.actor") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.userEmail ?? row.original.userName ?? "—"}
        </span>
      ),
    },
    {
      id: "timestamp",
      accessorFn: (row) => row.timestamp ?? "",
      header: t("auditLogs.timestamp"),
      meta: { label: t("auditLogs.timestamp") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.timestamp)}
        </span>
      ),
    },
    {
      id: "actions",
      enableSorting: false,
      enableHiding: false,
      header: () => <span className="sr-only">{t("common.actions")}</span>,
      cell: ({ row }) => (
        <div className="text-end">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={t("common.view")}
            onClick={() => setDetail(row.original)}
          >
            <Eye />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <PageHeader
        title={t("auditLogs.title")}
        description={t("auditLogs.subtitle")}
        actions={
          canExport ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" disabled={exportMutation.isPending}>
                  <Download data-icon="inline-start" />
                  {t("auditLogs.export")}
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuGroup>
                  <DropdownMenuItem
                    onClick={() => exportMutation.mutate("csv")}
                  >
                    CSV
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => exportMutation.mutate("json")}
                  >
                    JSON
                  </DropdownMenuItem>
                </DropdownMenuGroup>
              </DropdownMenuContent>
            </DropdownMenu>
          ) : null
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
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
        <DateRangePicker
          from={fromDate || undefined}
          to={toDate || undefined}
          onChange={({ from, to }) =>
            setFilters({ from: from ?? "", to: to ?? "" })
          }
        />
      </div>

      <DataTable
        fillHeight
        tableId="audit-logs"
        columns={columns}
        data={query.data?.logs ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        // Audit logs keep their dedicated server-side export (CSV/JSON, in the
        // page header) and the JSON-diff detail dialog (Eye action).
        enableExport={false}
        enableRowDetail={false}
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

      {detail ? (
        <AuditLogDetailDialog
          open={Boolean(detail)}
          onOpenChange={(open) => !open && setDetail(undefined)}
          log={detail}
        />
      ) : null}
    </div>
  )
}
