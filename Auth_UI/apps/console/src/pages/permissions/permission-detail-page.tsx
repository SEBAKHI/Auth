import { useQuery } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"

import { DetailList } from "@authsystem/ui/common/detail-list"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { api } from "@authsystem/api/client"
import {
  collectAllPages,
  toSortParams,
  unwrap,
  toNumber,
} from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import { permissionHref, userHref } from "@/lib/record-hrefs"
import { formatDateTime, fullName, userStatusMeta } from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import {
  enumArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { useTabParam } from "@authsystem/ui/hooks/use-tab-param"
import type { Schemas } from "@authsystem/api/types"
import { PermissionFormDialog } from "./permission-form-dialog"

type PermissionUsersUrlFilters = {
  status: Array<"active" | "inactive" | "locked" | "pending">
}

const PERMISSION_USERS_URL_OPTIONS = {
  namespace: "users",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.permissionUsers,
  defaultSorting: [{ id: "email", desc: false }],
  filters: {
    status: enumArrayUrlFilter(["active", "inactive", "locked", "pending"]),
  },
} satisfies ListUrlStateOptions<PermissionUsersUrlFilters>

const PERMISSION_DETAIL_TABS = ["users", "implications"] as const

function grantSources(
  row: Schemas["PermissionUserDto"],
  t: (k: string) => string
) {
  const sources: string[] = []
  if (row.viaDirect) sources.push(t("permissions.grant.direct"))
  if (row.viaOrganization) sources.push(t("permissions.grant.organization"))
  if (row.viaRole) sources.push(t("permissions.grant.role"))
  return sources
}

function PermissionUsersTab({ permissionId }: { permissionId: string }) {
  const { t } = useTranslation()
  const {
    pageIndex: page,
    pageSize,
    search: searchInput,
    sorting,
    filters,
    setSearch: setSearchInput,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilters,
  } = useListUrlState(PERMISSION_USERS_URL_OPTIONS)
  const search = useDebouncedValue(searchInput)
  const { sortBy, sortDirection } = toSortParams(sorting)
  const columnFilters: ColumnFiltersState = filters.status.length
    ? [{ id: "status", value: filters.status }]
    : []
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      status:
        (next.find((filter) => filter.id === "status")?.value as
          | PermissionUsersUrlFilters["status"]
          | undefined) ?? [],
    })

  const query = useQuery({
    queryKey: [
      "permission-users",
      permissionId,
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Permissions/{id}/users", {
          params: {
            path: { id: permissionId },
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

  const exportAll = React.useCallback(
    () =>
      collectAllPages<Schemas["PermissionUserDto"]>(
        async (pageNumber, size) => {
          const result = await unwrap(
            api.GET("/api/v1/Permissions/{id}/users", {
              params: {
                path: { id: permissionId },
                query: {
                  pageNumber,
                  pageSize: size,
                  searchTerm: search || undefined,
                  sortBy,
                  sortDirection,
                },
              },
            })
          )
          return {
            items: result.users ?? [],
            totalCount: toNumber(result.totalCount),
          }
        }
      ),
    [permissionId, search, sortBy, sortDirection]
  )

  const columns: ColumnDef<Schemas["PermissionUserDto"], unknown>[] = [
    avatarColumn<Schemas["PermissionUserDto"]>({
      getSrc: (row) => row.profileImageUrl,
      getName: (row) =>
        row.displayName ||
        fullName(row.firstName, row.lastName, row.email ?? ""),
      covers: ["profileImageUrl"],
    }),
    {
      id: "firstName",
      accessorFn: (row) =>
        row.displayName ||
        fullName(row.firstName, row.lastName, row.email ?? ""),
      header: t("common.name"),
      meta: {
        label: t("common.name"),
        covers: ["displayName", "fullName", "lastName", "email"],
      },
      cell: ({ row }) => (
        <RecordLink
          href={userHref(row.original.userId)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">
            {row.original.displayName ||
              fullName(
                row.original.firstName,
                row.original.lastName,
                row.original.email ?? ""
              )}
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.email}
          </p>
        </RecordLink>
      ),
    },
    {
      id: "grantSource",
      enableSorting: false,
      accessorFn: (row) => grantSources(row, t).join(", "),
      header: t("permissions.grantSource"),
      meta: {
        label: t("permissions.grantSource"),
        covers: ["viaDirect", "viaOrganization", "viaRole"],
      },
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {grantSources(row.original, t).map((source) => (
            <Badge key={source} variant="outline">
              {source}
            </Badge>
          ))}
        </div>
      ),
    },
    {
      id: "roleNames",
      enableSorting: false,
      accessorFn: (row) => row.roleNames ?? "",
      header: t("permissions.viaRoles"),
      meta: { label: t("permissions.viaRoles") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.roleNames ?? "—"}
        </span>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => userStatusMeta(row.status).key,
      filterFn: "faceted",
      header: t("common.status"),
      meta: {
        label: t("common.status"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "active", label: t("common.active") },
          { value: "inactive", label: t("common.inactive") },
          { value: "locked", label: t("common.locked") },
          { value: "pending", label: t("common.pending") },
        ],
      },
      cell: ({ row }) => {
        const meta = userStatusMeta(row.original.status)
        return <Badge variant={meta.variant}>{t(`common.${meta.key}`)}</Badge>
      },
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <SearchInput
        value={searchInput}
        onChange={setSearchInput}
        placeholder={t("users.searchPlaceholder")}
      />
      <DataTable
        tableId="permission-users"
        columns={columns}
        data={query.data?.users ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onExportAll={exportAll}
        enableRowDetail={false}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
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

function PermissionImplicationsTab({ permissionId }: { permissionId: string }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["permissions", permissionId, "implications"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Permissions/{id}/implications", {
          params: { path: { id: permissionId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["PermissionDto"], unknown>[] = [
    {
      id: "code",
      accessorFn: (row) => row.code ?? "",
      header: t("common.code"),
      meta: { label: t("common.code") },
      cell: ({ row }) => (
        <RecordLink
          href={permissionHref(row.original.id)}
          className="text-start"
        >
          <span className="font-mono text-sm">{row.original.code}</span>
        </RecordLink>
      ),
    },
    {
      accessorKey: "name",
      header: t("common.name"),
      meta: { label: t("common.name") },
    },
    {
      id: "description",
      accessorFn: (row) => row.description ?? "",
      header: t("common.description"),
      meta: { label: t("common.description") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.description ?? "—"}
        </span>
      ),
    },
  ]

  return (
    <DataTable
      tableId="permission-implications"
      globalSearch
      columns={columns}
      data={query.data ?? []}
      isLoading={query.isLoading}
      error={query.isError ? query.error : undefined}
      onRetry={() => query.refetch()}
      enableRowDetail={false}
    />
  )
}

export function PermissionDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const permissionId = id as string
  const { hasPermission } = useAuth()
  const [activeTab, setActiveTab] = useTabParam(PERMISSION_DETAIL_TABS)
  const canUpdate = hasPermission(PERMISSIONS.permissions.update)
  const [editOpen, setEditOpen] = React.useState(false)

  const detailQuery = useQuery({
    queryKey: ["permissions", permissionId],
    enabled: Boolean(permissionId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Permissions/{id}", {
          params: { path: { id: permissionId } },
        })
      ),
  })
  const permission = detailQuery.data
  usePageBreadcrumb(permission?.name)

  return (
    <div className="flex flex-col gap-6">
      {detailQuery.isLoading || !permission ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={permission.name ?? "—"}
            description={permission.code}
            actions={
              canUpdate ? (
                <Button variant="outline" onClick={() => setEditOpen(true)}>
                  {t("common.edit")}
                </Button>
              ) : null
            }
          />
          <DetailList
            items={[
              {
                label: t("common.description"),
                value: permission.description,
                fullWidth: true,
              },
              {
                label: t("nav.applications"),
                value: permission.applicationName ?? "—",
              },
              {
                label: t("common.status"),
                value: (
                  <Badge
                    variant={permission.isActive ? "default" : "secondary"}
                  >
                    {permission.isActive
                      ? t("common.active")
                      : t("common.inactive")}
                  </Badge>
                ),
              },
              {
                label: t("permissions.level"),
                value: toNumber(permission.level),
              },
              {
                label: t("permissions.wildcard"),
                value: permission.isWildcard ? t("common.yes") : t("common.no"),
              },
              {
                label: t("common.createdAt"),
                value: formatDateTime(permission.createdAt),
              },
              { label: t("common.createdBy"), value: permission.createdByName },
              {
                label: t("common.modifiedAt"),
                value: formatDateTime(permission.modifiedAt),
              },
              {
                label: t("common.modifiedBy"),
                value: permission.modifiedByName,
              },
            ]}
          />
        </>
      )}

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList>
          <TabsTrigger value="users">{t("nav.users")}</TabsTrigger>
          <TabsTrigger value="implications">
            {t("permissions.implications")}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="mt-4">
          <PermissionUsersTab permissionId={permissionId} />
        </TabsContent>
        <TabsContent value="implications" className="mt-4">
          <PermissionImplicationsTab permissionId={permissionId} />
        </TabsContent>
      </Tabs>

      {permission ? (
        <PermissionFormDialog
          open={editOpen}
          onOpenChange={setEditOpen}
          permission={permission}
          defaultApplicationId={permission.applicationId ?? undefined}
        />
      ) : null}
    </div>
  )
}
