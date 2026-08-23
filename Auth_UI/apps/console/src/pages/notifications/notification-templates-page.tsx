import { useQuery } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import { Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { toNumber, toSortParams, unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { formatDateTime } from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import {
  enumArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import { notificationTemplateHref } from "@/lib/record-hrefs"
import { CreateTemplateDialog } from "./components/create-template-dialog"
import { NotificationsTabs } from "./components/notifications-tabs"
import type { NotificationTemplateDto } from "./lib"

type TemplateListFilters = {
  channels: Array<"Email">
  statuses: Array<"published" | "unpublished">
}

const TEMPLATE_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.notificationTemplates,
  defaultSorting: [{ id: "typeName", desc: false }],
  filters: {
    channels: enumArrayUrlFilter(["Email"], "channel"),
    statuses: enumArrayUrlFilter(["published", "unpublished"], "status"),
  },
} satisfies ListUrlStateOptions<TemplateListFilters>

/**
 * Admin list of notification templates. All message content lives in the
 * database; templates are edited, previewed, and published here without any
 * redeploy.
 */
export function NotificationTemplatesPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const {
    pageIndex: page,
    pageSize,
    search: searchInput,
    sorting,
    filters: { channels, statuses },
    setSearch: setSearchInput,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilters,
  } = useListUrlState(TEMPLATE_LIST_URL_OPTIONS)
  const search = useDebouncedValue(searchInput)
  const { sortBy, sortDirection } = toSortParams(sorting)
  const [createOpen, setCreateOpen] = React.useState(false)

  const columnFilters: ColumnFiltersState = [
    ...(channels.length ? [{ id: "channel", value: channels }] : []),
    ...(statuses.length ? [{ id: "status", value: statuses }] : []),
  ]
  const onColumnFiltersChange = (next: ColumnFiltersState) => {
    setFilters({
      channels:
        (next.find((filter) => filter.id === "channel")?.value as
          | Array<"Email">
          | undefined) ?? [],
      statuses:
        (next.find((filter) => filter.id === "status")?.value as
          | Array<"published" | "unpublished">
          | undefined) ?? [],
    })
  }

  const channel = channels.length === 1 ? 1 : undefined
  const isPublished =
    statuses.length === 1 ? statuses[0] === "published" : undefined

  const canManage = hasPermission(PERMISSIONS.notificationTemplates.manage)

  const query = useQuery({
    queryKey: [
      "notification-templates",
      { page, pageSize, search, channel, isPublished, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-templates", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              channel,
              isPublished,
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
          <RecordLink
            href={notificationTemplateHref(template.id)}
            className="min-w-0 text-start"
          >
            {/* The direction belongs on an inline `bdi`, not on the `p`: `dir` on
                a block re-resolves the inherited `text-align: start` against that
                block's own direction, which left-aligned the code line while the
                title above it stayed right-aligned. */}
            <p className="truncate font-medium">
              <bdi dir="auto">{template.typeName}</bdi>
            </p>
            <p className="truncate text-xs text-muted-foreground">
              <bdi dir="ltr">{template.typeCode}</bdi>
            </p>
          </RecordLink>
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
        filterOptions: [
          { value: "Email", label: t("notifications.channelEmail") },
        ],
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
      // Filterable, but not in the endpoint's sortBy allow-list.
      enableSorting: false,
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
              <Badge variant="destructive">
                {t("notifications.systemBadge")}
              </Badge>
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
        <span className="text-sm text-muted-foreground uppercase" dir="ltr">
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
    <div className="flex min-h-0 flex-1 flex-col gap-6">
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
        onChange={setSearchInput}
        placeholder={t("notifications.templatesSearchPlaceholder")}
      />

      <DataTable
        fillHeight
        tableId="notification-templates"
        columns={columns}
        data={query.data?.templates ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        sorting={sorting}
        onSortingChange={setSorting}
        enableRowDetail={false}
        pagination={{
          pageIndex: page,
          pageSize,
          pageCount: toNumber(query.data?.totalPages),
          totalCount: toNumber(query.data?.totalCount),
          onPageChange: setPage,
          onPageSizeChange: setPageSize,
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
