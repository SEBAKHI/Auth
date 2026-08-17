import { useQuery } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"

import { DetailList } from "@authsystem/ui/common/detail-list"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { api } from "@authsystem/api/client"
import { collectAllPages, toSortParams, unwrap, toNumber } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { formatDateTime, fullName, userStatusMeta } from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import type { Schemas } from "@authsystem/api/types"
import { RoleFormDialog } from "./role-form-dialog"
import { RolePermissionsDialog } from "./role-permissions-dialog"

function RoleUsersTab({ roleId }: { roleId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "email", desc: false },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const query = useQuery({
    queryKey: [
      "role-users",
      roleId,
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Roles/{id}/users", {
          params: {
            path: { id: roleId },
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
      collectAllPages<Schemas["RoleUserDto"]>(async (pageNumber, size) => {
        const result = await unwrap(
          api.GET("/api/v1/Roles/{id}/users", {
            params: {
              path: { id: roleId },
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
      }),
    [roleId, search, sortBy, sortDirection]
  )

  const columns: ColumnDef<Schemas["RoleUserDto"], unknown>[] = [
    avatarColumn<Schemas["RoleUserDto"]>({
      getSrc: (row) => row.profileImageUrl,
      getName: (row) =>
        row.displayName ||
        fullName(row.firstName, row.lastName, row.email ?? ""),
    }),
    {
      id: "firstName",
      accessorFn: (row) =>
        row.displayName || fullName(row.firstName, row.lastName, row.email ?? ""),
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/users/${row.original.userId}`)}
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
        </button>
      ),
    },
    {
      id: "assignmentSource",
      enableSorting: false,
      accessorFn: (row) => row.assignmentSource ?? "",
      header: t("roles.assignmentSource"),
      meta: { label: t("roles.assignmentSource") },
      cell: ({ row }) => (
        <Badge variant="outline">
          {t(`roles.assignment.${row.original.assignmentSource ?? "direct"}`)}
        </Badge>
      ),
    },
    {
      id: "organizationNames",
      enableSorting: false,
      accessorFn: (row) => row.organizationNames ?? "",
      header: t("nav.organizations"),
      meta: { label: t("nav.organizations") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.organizationNames ?? "—"}
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
    {
      id: "lastLoginAt",
      accessorFn: (row) => row.lastLoginAt ?? "",
      header: t("users.lastLogin"),
      meta: { label: t("users.lastLogin") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.lastLoginAt)}
        </span>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <SearchInput
        value={searchInput}
        onChange={(value) => {
            setSearchInput(value)
            setPage(0)
          }}
        placeholder={t("users.searchPlaceholder")}
      />
      <DataTable
        tableId="role-users"
        columns={columns}
        data={query.data?.users ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onExportAll={exportAll}
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
    </div>
  )
}

function RoleApplicationsTab({ roleId }: { roleId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["role-apps", roleId],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Roles/{id}/applications", {
          params: { path: { id: roleId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["RoleApplicationDto"], unknown>[] = [
    avatarColumn<Schemas["RoleApplicationDto"]>({
      getSrc: (row) => row.logoUrl,
      getName: (row) => row.name,
      fit: "contain",
    }),
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/applications/${row.original.applicationId}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </button>
      ),
    },
    {
      id: "relationship",
      accessorFn: (row) => row.relationship ?? "",
      filterFn: "faceted",
      header: t("roles.relationship"),
      meta: { label: t("roles.relationship"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <Badge variant="outline">
          {t(`roles.relation.${row.original.relationship ?? "assigned"}`)}
        </Badge>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => (row.isActive ? "active" : "inactive"),
      filterFn: "faceted",
      header: t("common.status"),
      meta: {
        label: t("common.status"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "active", label: t("common.active") },
          { value: "inactive", label: t("common.inactive") },
        ],
      },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "default" : "secondary"}>
          {row.original.isActive ? t("common.active") : t("common.inactive")}
        </Badge>
      ),
    },
  ]

  return (
    <DataTable
      tableId="role-apps"
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

function RolePermissionsTab({
  roleId,
  roleName,
  permissions,
  canUpdate,
}: {
  roleId: string
  roleName: string
  permissions: string[]
  canUpdate: boolean
}) {
  const { t } = useTranslation()
  const [manageOpen, setManageOpen] = React.useState(false)

  const data = React.useMemo(
    () => permissions.map((code) => ({ code })),
    [permissions]
  )

  const columns: ColumnDef<{ code: string }, unknown>[] = [
    {
      accessorKey: "code",
      header: t("common.code"),
      meta: { label: t("common.code") },
      cell: ({ row }) => (
        <span className="font-mono text-sm">{row.original.code}</span>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      {canUpdate ? (
        <div className="flex justify-end">
          <Button variant="outline" onClick={() => setManageOpen(true)}>
            {t("users.managePermissions")}
          </Button>
        </div>
      ) : null}
      <DataTable
        tableId="role-perms"
        globalSearch
        columns={columns}
        data={data}
        emptyMessage={t("common.empty")}
        enableRowDetail={false}
      />
      {canUpdate ? (
        <RolePermissionsDialog
          open={manageOpen}
          onOpenChange={setManageOpen}
          roleId={roleId}
          roleName={roleName}
          grantedCodes={permissions}
        />
      ) : null}
    </div>
  )
}

export function RoleDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const roleId = id as string
  const { hasPermission } = useAuth()
  const canUpdate = hasPermission(PERMISSIONS.roles.update)
  const [editOpen, setEditOpen] = React.useState(false)

  const detailQuery = useQuery({
    queryKey: ["roles", roleId],
    enabled: Boolean(roleId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Roles/{id}", { params: { path: { id: roleId } } })
      ),
  })
  const role = detailQuery.data
  usePageBreadcrumb(role?.name)

  return (
    <div className="flex flex-col gap-6">
      {detailQuery.isLoading || !role ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={role.name ?? "—"}
            description={role.code}
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
                value: role.description,
                fullWidth: true,
              },
              {
                label: t("nav.applications"),
                value: role.applicationName ?? "—",
              },
              {
                label: t("roles.system"),
                value: role.isSystem ? t("common.yes") : t("common.no"),
              },
              {
                label: t("common.status"),
                value: (
                  <Badge variant={role.isActive ? "default" : "secondary"}>
                    {role.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                ),
              },
              { label: t("roles.level"), value: toNumber(role.level) },
              {
                label: t("common.createdAt"),
                value: formatDateTime(role.createdAt),
              },
              { label: t("common.createdBy"), value: role.createdByName },
              {
                label: t("common.modifiedAt"),
                value: formatDateTime(role.modifiedAt),
              },
              { label: t("common.modifiedBy"), value: role.modifiedByName },
            ]}
          />
        </>
      )}

      <Tabs defaultValue="users">
        <TabsList>
          <TabsTrigger value="users">{t("nav.users")}</TabsTrigger>
          <TabsTrigger value="applications">
            {t("nav.applications")}
          </TabsTrigger>
          <TabsTrigger value="permissions">{t("nav.permissions")}</TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="mt-4">
          <RoleUsersTab roleId={roleId} />
        </TabsContent>
        <TabsContent value="applications" className="mt-4">
          <RoleApplicationsTab roleId={roleId} />
        </TabsContent>
        <TabsContent value="permissions" className="mt-4">
          <RolePermissionsTab
            roleId={roleId}
            roleName={role?.name ?? ""}
            permissions={role?.permissions ?? []}
            canUpdate={canUpdate}
          />
        </TabsContent>
      </Tabs>

      {role ? (
        <RoleFormDialog
          open={editOpen}
          onOpenChange={setEditOpen}
          role={role}
          defaultApplicationId={role.applicationId ?? undefined}
        />
      ) : null}
    </div>
  )
}
