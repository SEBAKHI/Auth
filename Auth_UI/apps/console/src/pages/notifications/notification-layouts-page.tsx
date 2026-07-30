import { useQuery } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { PageHeader } from "@astoom/ui/common/page-header"
import { SearchInput } from "@astoom/ui/common/search-input"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { formatDateTime } from "@astoom/ui/format"
import { PERMISSIONS } from "@/lib/constants"
import { CreateLayoutDialog } from "./components/create-layout-dialog"
import { NotificationsTabs } from "./components/notifications-tabs"
import type { NotificationLayoutDto } from "./lib"

/**
 * Notification layouts: the shared visual identity (header/footer/CSS) wrapped
 * around every template body. One layout per (application, channel) scope; all
 * languages share it, with direction injected per message.
 */
export function NotificationLayoutsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const [createOpen, setCreateOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")

  const canManage = hasPermission(PERMISSIONS.notificationLayouts.manage)

  const query = useQuery({
    queryKey: ["notification-layouts"],
    queryFn: () => unwrap(api.GET("/api/v1/notification-layouts")),
  })

  // The layout list is small and unpaged, so filtering happens client-side.
  const filtered = React.useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return query.data ?? []
    return (query.data ?? []).filter(
      (layout) =>
        layout.name?.toLowerCase().includes(term) ||
        layout.applicationName?.toLowerCase().includes(term)
    )
  }, [query.data, search])

  const columns: ColumnDef<NotificationLayoutDto, unknown>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/notifications/layouts/${row.original.id}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
        </button>
      ),
    },
    {
      id: "applicationName",
      accessorFn: (row) => row.applicationName ?? "",
      header: t("notifications.application"),
      meta: { label: t("notifications.application") },
      cell: ({ row }) =>
        row.original.applicationName ? (
          <span className="text-sm">{row.original.applicationName}</span>
        ) : (
          <Badge variant="outline">{t("notifications.global")}</Badge>
        ),
    },
    {
      id: "status",
      accessorFn: (row) => (row.isPublished ? "published" : "unpublished"),
      header: t("notifications.status"),
      meta: { label: t("notifications.status") },
      cell: ({ row }) => (
        <div className="flex flex-wrap items-center gap-1">
          {row.original.isPublished ? (
            <Badge>{t("notifications.published")}</Badge>
          ) : (
            <Badge variant="outline">{t("notifications.unpublished")}</Badge>
          )}
          {row.original.hasUnpublishedChanges ? (
            <Badge variant="secondary">{t("notifications.unpublishedChanges")}</Badge>
          ) : null}
        </div>
      ),
    },
    {
      id: "modifiedAt",
      accessorFn: (row) => row.modifiedAt ?? "",
      header: t("common.modifiedAt"),
      meta: { label: t("common.modifiedAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.modifiedAt ?? row.original.createdAt)}
        </span>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("notifications.layoutsTitle")}
        description={t("notifications.layoutsSubtitle")}
        actions={
          canManage ? (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus data-icon="inline-start" />
              {t("notifications.newLayout")}
            </Button>
          ) : null
        }
      />

      <NotificationsTabs />

      <SearchInput
        value={search}
        onChange={setSearch}
        placeholder={t("notifications.layoutsSearchPlaceholder")}
      />

      <DataTable
        tableId="notification-layouts"
        columns={columns}
        data={filtered}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        enableRowDetail={false}
      />

      <CreateLayoutDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  )
}
