import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { RotateCcw } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { toNumber, toSortParams, unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { directionForLanguage } from "@astoom/i18n"
import type { Schemas } from "@astoom/api/types"
import { PageHeader } from "@astoom/ui/common/page-header"
import { SearchInput } from "@astoom/ui/common/search-input"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { formatDateTime } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { NotificationsTabs } from "./components/notifications-tabs"
import { OutboxMessageSheet } from "./components/outbox-message-sheet"

type OutboxMessageDto = Schemas["NotificationOutboxMessageDto"]

const STATUS_BADGES: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Pending: "outline",
  Processing: "secondary",
  Sent: "default",
  Retry: "secondary",
  Dead: "destructive",
}

/**
 * The delivery log: every message the notification system enqueued — what was
 * sent, to whom, in which language, by which template version, its delivery
 * status and errors. Failed messages can be requeued for immediate retry.
 * (Rows appear here only when Notifications:UseOutbox is enabled.)
 */
export function NotificationOutboxPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "createdAt", desc: true },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)
  const [selected, setSelected] = React.useState<OutboxMessageDto | undefined>()

  const canManage = hasPermission(PERMISSIONS.notificationTemplates.manage)

  const query = useQuery({
    queryKey: [
      "notification-outbox",
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-outbox", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              searchTerm: search || undefined,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  const retryMutation = useMutation({
    mutationFn: (id: string) =>
      unwrap(
        api.POST("/api/v1/notification-outbox/{id}/retry", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.outboxRetried"))
      void queryClient.invalidateQueries({ queryKey: ["notification-outbox"] })
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<OutboxMessageDto, unknown>[] = [
    {
      id: "typeCode",
      accessorFn: (row) => row.notificationTypeCode ?? "",
      header: t("notifications.type"),
      meta: { label: t("notifications.type") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => setSelected(row.original)}
        >
          {/* Direction on an inline `bdi`, never on the `p`: `dir` on a block
              re-resolves the inherited `text-align: start` and left-aligns the
              line inside an RTL table. */}
          <p className="truncate font-medium">
            <bdi dir="ltr">{row.original.notificationTypeCode}</bdi>
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {/* The row already knows the send's locale, so bind the subject's
                direction to it instead of guessing from the text. */}
            <bdi dir={directionForLanguage(row.original.languageCode ?? "")}>
              {row.original.subject}
            </bdi>
          </p>
        </button>
      ),
    },
    {
      id: "recipient",
      accessorFn: (row) => row.recipient ?? "",
      header: t("notifications.recipient"),
      meta: { label: t("notifications.recipient") },
      cell: ({ row }) => (
        <span className="text-sm" dir="ltr">
          {row.original.recipient}
        </span>
      ),
    },
    {
      id: "languageCode",
      accessorFn: (row) => row.languageCode ?? "",
      header: t("notifications.language"),
      meta: { label: t("notifications.language") },
      cell: ({ row }) => (
        <span className="text-sm uppercase text-muted-foreground" dir="ltr">
          {row.original.languageCode}
        </span>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => row.status ?? "",
      filterFn: "faceted",
      header: t("notifications.status"),
      meta: {
        label: t("notifications.status"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "Pending", label: t("notifications.outboxPending") },
          { value: "Processing", label: t("notifications.outboxProcessing") },
          { value: "Sent", label: t("notifications.outboxSent") },
          { value: "Retry", label: t("notifications.outboxRetry") },
          { value: "Dead", label: t("notifications.outboxDead") },
        ],
      },
      cell: ({ row }) => {
        const status = row.original.status ?? ""
        const statusLabels: Record<string, string> = {
          Pending: t("notifications.outboxPending"),
          Processing: t("notifications.outboxProcessing"),
          Sent: t("notifications.outboxSent"),
          Retry: t("notifications.outboxRetry"),
          Dead: t("notifications.outboxDead"),
        }
        return (
          <div className="flex items-center gap-2">
            <Badge variant={STATUS_BADGES[status] ?? "outline"}>
              {statusLabels[status] ?? status}
            </Badge>
            {toNumber(row.original.attemptCount) > 0 ? (
              <span className="text-xs text-muted-foreground">
                {t("notifications.attempts", {
                  count: toNumber(row.original.attemptCount),
                })}
              </span>
            ) : null}
          </div>
        )
      },
    },
    {
      id: "sentAt",
      accessorFn: (row) => row.sentAt ?? "",
      header: t("notifications.sentAt"),
      meta: { label: t("notifications.sentAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.sentAt
            ? formatDateTime(row.original.sentAt)
            : row.original.status === "Retry"
              ? t("notifications.nextAttemptAt", {
                  time: formatDateTime(row.original.nextAttemptAt),
                })
              : "—"}
        </span>
      ),
    },
    {
      id: "createdAt",
      accessorFn: (row) => row.createdAt ?? "",
      header: t("common.createdAt"),
      meta: { label: t("common.createdAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.createdAt)}
        </span>
      ),
    },
    ...(canManage
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => <span className="sr-only">{t("common.actions")}</span>,
            cell: ({ row }) => {
              const message = row.original
              const retryable = message.status === "Retry" || message.status === "Dead"
              return retryable ? (
                <div className="text-end">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t("notifications.retryNow")}
                    disabled={retryMutation.isPending}
                    onClick={() => message.id && retryMutation.mutate(message.id)}
                  >
                    <RotateCcw />
                  </Button>
                </div>
              ) : null
            },
          } satisfies ColumnDef<OutboxMessageDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("notifications.outboxTitle")}
        description={t("notifications.outboxSubtitle")}
      />

      <NotificationsTabs />

      <SearchInput
        value={searchInput}
        onChange={(value) => {
            setSearchInput(value)
            setPage(0)
          }}
        placeholder={t("notifications.outboxSearchPlaceholder")}
      />

      <DataTable
        tableId="notification-outbox"
        columns={columns}
        data={query.data?.messages ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        sorting={sorting}
        onSortingChange={(next) => {
          setSorting(next)
          setPage(0)
        }}
        enableRowDetail={false}
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

      {selected?.id ? (
        <OutboxMessageSheet
          messageId={selected.id}
          open={Boolean(selected)}
          onOpenChange={(open) => !open && setSelected(undefined)}
          canManage={canManage}
        />
      ) : null}
    </div>
  )
}
