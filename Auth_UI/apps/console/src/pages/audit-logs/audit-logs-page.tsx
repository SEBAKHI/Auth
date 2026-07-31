import { useMutation, useQuery } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { Download, Eye } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ApplicationSelect } from "@astoom/ui/common/application-select"
import { DateRangePicker } from "@astoom/ui/common/date-range-picker"
import { PageHeader } from "@astoom/ui/common/page-header"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Button } from "@astoom/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { Input } from "@astoom/ui/input"
import { api } from "@astoom/api/client"
import { toSortParams, unwrap, toNumber } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { DEFAULT_PAGE_SIZE, PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@astoom/api/errors"
import { formatDateTime } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import type { Schemas } from "@astoom/api/types"
import { AuditLogDetailDialog } from "./audit-log-detail-dialog"

type AuditLogDto = Schemas["AuditLogDto"]

function startOfDay(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString()
}
function endOfDay(date: string): string {
  return new Date(`${date}T23:59:59`).toISOString()
}

export function AuditLogsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [applicationId, setApplicationId] = React.useState<string>()
  const [actionInput, setActionInput] = React.useState("")
  const action = useDebouncedValue(actionInput)
  const [fromDate, setFromDate] = React.useState("")
  const [toDate, setToDate] = React.useState("")
  const [detail, setDetail] = React.useState<AuditLogDto | undefined>()
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "timestamp", desc: true },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const canExport = hasPermission(PERMISSIONS.auditLogs.export)

  const filters = React.useMemo(
    () => ({
      applicationId,
      action: action || undefined,
      fromDate: fromDate ? startOfDay(fromDate) : undefined,
      toDate: toDate ? endOfDay(toDate) : undefined,
    }),
    [applicationId, action, fromDate, toDate]
  )

  const query = useQuery({
    queryKey: ["audit-logs", { page, pageSize, filters, sortBy, sortDirection }],
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
        // Export in the same order the table currently shows.
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
          <p className="truncate font-medium">{row.original.action}</p>
          {row.original.entityType ? (
            <p className="truncate text-xs text-muted-foreground">
              {row.original.entityType}
            </p>
          ) : null}
        </div>
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
    <div className="flex flex-col gap-6">
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

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <ApplicationSelect
          value={applicationId}
          onChange={(value) => {
            setApplicationId(value)
            setPage(0)
          }}
          allowAll
          className="w-full"
        />
        <Input
          value={actionInput}
          onChange={(e) => {
            setActionInput(e.target.value)
            setPage(0)
          }}
          placeholder={t("auditLogs.searchAction")}
        />
        <DateRangePicker
          from={fromDate || undefined}
          to={toDate || undefined}
          onChange={({ from, to }) => {
            setFromDate(from ?? "")
            setToDate(to ?? "")
            setPage(0)
          }}
        />
      </div>

      <DataTable
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
        onSortingChange={(next) => {
          setSorting(next)
          setPage(0)
        }}
        pagination={{
          pageIndex: page,
          pageSize,
          pageCount: toNumber(query.data?.totalPages),
          totalCount: toNumber(query.data?.totalCount),
          onPageChange: setPage,
          onPageSizeChange: (size) => {
            setPageSize(size)
            setPage(0)
          },
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
