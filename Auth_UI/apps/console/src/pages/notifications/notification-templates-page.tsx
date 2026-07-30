import { useQuery } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { api } from "@astoom/api/client"
import { toNumber, toSortParams, unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { PageHeader } from "@astoom/ui/common/page-header"
import { SearchInput } from "@astoom/ui/common/search-input"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { formatDateTime } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { CreateTemplateDialog } from "./components/create-template-dialog"
import { NotificationsTabs } from "./components/notifications-tabs"
import type { NotificationTemplateDto } from "./lib"

/**
 * Admin list of notification templates. All message content lives in the
 * database; templates are edited, previewed, and published here without any
 * redeploy.
 */
export function NotificationTemplatesPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "typeName", desc: false },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)
  const [createOpen, setCreateOpen] = React.useState(false)

  const canManage = hasPermission(PERMISSIONS.notificationTemplates.manage)

  const query = useQuery({
    queryKey: [
      "notification-templates",
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-templates", {
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

  const typesQuery = useQuery({
    queryKey: ["notification-types"],
    queryFn: () => unwrap(api.GET("/api/v1/notification-types")),
  })

  const columns: ColumnDef<NotificationTemplateDto, unknown>[] = [
    {
      id: "typeName",
      accessorFn: (row) => row.typeName ?? "",
      header: t("notifications.type"),
      meta: { label: t("notifications.type") },
      cell: ({ row }) => {
        const template = row.original
        return (
          <button
            type="button"
            className="min-w-0 text-start hover:underline"
            onClick={() => navigate(`/notifications/templates/${template.id}`)}
          >
            <p className="truncate font-medium">{template.typeName}</p>
            <p className="truncate text-xs text-muted-foreground" dir="ltr">
              {template.typeCode}
            </p>
          </button>
        )
      },
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
      id: "channel",
      accessorFn: (row) => row.channel ?? "",
      filterFn: "faceted",
      header: t("notifications.channel"),
      meta: {
        label: t("notifications.channel"),
        filterVariant: "faceted",
        filterOptions: [{ value: "Email", label: t("notifications.channelEmail") }],
      },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.channel === "Email"
            ? t("notifications.channelEmail")
            : row.original.channel}
        </span>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => (row.isPublished ? "published" : "unpublished"),
      filterFn: "faceted",
      header: t("notifications.status"),
      meta: {
        label: t("notifications.status"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "published", label: t("notifications.published") },
          { value: "unpublished", label: t("notifications.unpublished") },
        ],
      },
      cell: ({ row }) => {
        const template = row.original
        return (
          <div className="flex flex-wrap items-center gap-1">
            {template.isPublished ? (
              <Badge>
                {t("notifications.publishedVersion", {
                  version: template.publishedVersionNumber ?? 0,
                })}
              </Badge>
            ) : (
              <Badge variant="outline">{t("notifications.unpublished")}</Badge>
            )}
            {template.hasDraft ? (
              <Badge variant="secondary">{t("notifications.draft")}</Badge>
            ) : null}
            {template.typeIsSystem && !template.applicationId ? (
              <Badge variant="destructive">{t("notifications.systemBadge")}</Badge>
            ) : null}
          </div>
        )
      },
    },
    {
      id: "defaultLanguage",
      accessorFn: (row) => row.defaultLanguage ?? "",
      header: t("notifications.defaultLanguage"),
      meta: { label: t("notifications.defaultLanguage") },
      cell: ({ row }) => (
        <span className="text-sm uppercase text-muted-foreground" dir="ltr">
          {row.original.defaultLanguage}
        </span>
      ),
    },
    {
      id: "translations",
      accessorFn: (row) => row.translationCount ?? 0,
      enableSorting: false,
      header: t("notifications.translations"),
      meta: { label: t("notifications.translations") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.translationCount ?? 0}/7
        </span>
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
        title={t("notifications.title")}
        description={t("notifications.subtitle")}
        actions={
          canManage ? (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus data-icon="inline-start" />
              {t("notifications.newTemplate")}
            </Button>
          ) : null
        }
      />

      <NotificationsTabs />

      <SearchInput
        value={searchInput}
        onChange={(value) => {
            setSearchInput(value)
            setPage(0)
          }}
        placeholder={t("notifications.templatesSearchPlaceholder")}
      />

      <DataTable
        tableId="notification-templates"
        columns={columns}
        data={query.data?.templates ?? []}
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

      <CreateTemplateDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        types={typesQuery.data ?? []}
      />
    </div>
  )
}
