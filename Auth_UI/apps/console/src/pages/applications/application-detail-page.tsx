import { useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { Search } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"

import { DetailList } from "@astoom/ui/common/detail-list"
import { LogoAvatar } from "@astoom/ui/common/logo-avatar"
import { PageHeader } from "@astoom/ui/common/page-header"
import { avatarColumn } from "@astoom/ui/data-table/columns"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { api } from "@astoom/api/client"
import { collectAllPages, toSortParams, unwrap, toNumber } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { usePageBreadcrumb } from "@astoom/ui/crumbs"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { formatDateTime, fullName, userStatusMeta } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import type { Schemas } from "@astoom/api/types"
import { ApplicationEditDialog } from "./application-dialogs"

function ApplicationUsersTab({ appId }: { appId: string }) {
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
      "app-users",
      appId,
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/users", {
          params: {
            path: { id: appId },
            query: {
              pageNumber: page + 1,
              pageSize,
              search: search || undefined,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  const exportAll = React.useCallback(
    () =>
      collectAllPages<Schemas["ApplicationUserDto"]>(async (pageNumber, size) => {
        const result = await unwrap(
          api.GET("/api/v1/Applications/{id}/users", {
            params: {
              path: { id: appId },
              query: {
                pageNumber,
                pageSize: size,
                search: search || undefined,
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
    [appId, search, sortBy, sortDirection]
  )

  const columns: ColumnDef<Schemas["ApplicationUserDto"], unknown>[] = [
    avatarColumn<Schemas["ApplicationUserDto"]>({
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
      id: "roleNames",
      enableSorting: false,
      accessorFn: (row) => row.roleNames ?? "",
      header: t("users.roles"),
      meta: { label: t("users.roles") },
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
    <div className="space-y-4">
      <div className="relative max-w-sm">
        <Search className="absolute start-2.5 top-2.5 size-4 text-muted-foreground" />
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value)
            setPage(0)
          }}
          placeholder={t("users.searchPlaceholder")}
          className="ps-8"
        />
      </div>
      <DataTable
        tableId="app-users"
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

function ApplicationOrganizationsTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "name", desc: false },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const query = useQuery({
    queryKey: ["app-orgs", appId, { page, pageSize, sortBy, sortDirection }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/organizations", {
          params: {
            path: { id: appId },
            query: { pageNumber: page + 1, pageSize, sortBy, sortDirection },
          },
        })
      ),
  })

  const columns: ColumnDef<Schemas["ApplicationOrganizationDto"], unknown>[] = [
    avatarColumn<Schemas["ApplicationOrganizationDto"]>({
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
          onClick={() => navigate(`/organizations/${row.original.organizationId}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </button>
      ),
    },
    {
      id: "memberCount",
      accessorFn: (row) => toNumber(row.memberCount),
      header: t("organizations.memberCount"),
      meta: { label: t("organizations.memberCount") },
      cell: ({ row }) => (
        <span className="text-sm tabular-nums">
          {toNumber(row.original.memberCount)}
        </span>
      ),
    },
    {
      id: "enabledAt",
      accessorFn: (row) => row.enabledAt ?? "",
      header: t("applications.enabledAt"),
      meta: { label: t("applications.enabledAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.enabledAt)}
        </span>
      ),
    },
    {
      id: "isActive",
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
      tableId="app-orgs"
      columns={columns}
      data={query.data?.organizations ?? []}
      isLoading={query.isLoading}
      error={query.isError ? query.error : undefined}
      onRetry={() => query.refetch()}
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
  )
}

function ApplicationRolesTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["applications", appId, "roles"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/roles", {
          params: { path: { id: appId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["RoleDto"], unknown>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/roles/${row.original.id}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </button>
      ),
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
    {
      id: "isSystem",
      accessorFn: (row) => (row.isSystem ? "system" : "custom"),
      filterFn: "faceted",
      header: t("roles.system"),
      meta: { label: t("roles.system"), filterVariant: "faceted" },
      cell: ({ row }) =>
        row.original.isSystem ? (
          <Badge variant="secondary">{t("roles.system")}</Badge>
        ) : (
          <span className="text-sm text-muted-foreground">—</span>
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
      tableId="app-roles"
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

function ApplicationPermissionsTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["applications", appId, "permissions"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/permissions", {
          params: { path: { id: appId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["PermissionDto"], unknown>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/permissions/${row.original.id}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </button>
      ),
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
      tableId="app-perms"
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

export function ApplicationDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const appId = id as string
  const { hasPermission } = useAuth()
  const canUpdate = hasPermission(PERMISSIONS.applications.update)
  const queryClient = useQueryClient()
  const [editOpen, setEditOpen] = React.useState(false)

  const detailQuery = useQuery({
    queryKey: ["applications", appId],
    enabled: Boolean(appId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}", {
          params: { path: { id: appId } },
        })
      ),
  })
  const app = detailQuery.data
  usePageBreadcrumb(app?.name)

  return (
    <div className="space-y-6">
      {detailQuery.isLoading || !app ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={app.name ?? "—"}
            description={app.code}
            leading={
              <LogoAvatar
                src={app.logoUrl}
                name={app.name}
                canEdit={canUpdate}
                successMessage={t("applications.updated")}
                invalidate={() => {
                  void queryClient.invalidateQueries({
                    queryKey: ["applications", appId],
                  })
                  void queryClient.invalidateQueries({
                    queryKey: ["applications"],
                  })
                }}
                persist={async (logoKey) => {
                  const { error } = await api.PUT(
                    "/api/v1/Applications/{id}",
                    {
                      params: { path: { id: appId } },
                      body: {
                        name: app.name ?? "",
                        description: app.description ?? null,
                        baseUrl: app.baseUrl ?? null,
                        logoUrl: logoKey,
                        contactEmail: app.contactEmail ?? null,
                        allowSelfRegistration:
                          app.allowSelfRegistration ?? false,
                        requireTwoFactor: app.requireTwoFactor ?? false,
                        requireEmailVerification:
                          app.requireEmailVerification ?? false,
                        sessionTimeoutMinutes:
                          app.sessionTimeoutMinutes ?? 60,
                        maxConcurrentSessions:
                          app.maxConcurrentSessions ?? 5,
                      },
                    }
                  )
                  if (error) throw error
                }}
              />
            }
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
                value: app.description,
                fullWidth: true,
              },
              {
                label: t("common.status"),
                value: (
                  <Badge variant={app.isActive ? "default" : "secondary"}>
                    {app.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                ),
              },
              { label: t("applications.baseUrl"), value: app.baseUrl },
              {
                label: t("applications.contactEmail"),
                value: app.contactEmail,
              },
              {
                label: t("applications.allowSelfRegistration"),
                value: app.allowSelfRegistration
                  ? t("common.yes")
                  : t("common.no"),
              },
              {
                label: t("applications.requireTwoFactor"),
                value: app.requireTwoFactor ? t("common.yes") : t("common.no"),
              },
              {
                label: t("applications.requireEmailVerification"),
                value: app.requireEmailVerification
                  ? t("common.yes")
                  : t("common.no"),
              },
              {
                label: t("applications.sessionTimeoutMinutes"),
                value: toNumber(app.sessionTimeoutMinutes),
              },
              {
                label: t("applications.maxConcurrentSessions"),
                value: toNumber(app.maxConcurrentSessions),
              },
              {
                label: t("common.createdAt"),
                value: formatDateTime(app.createdAt),
              },
              { label: t("common.createdBy"), value: app.createdByName },
              {
                label: t("common.modifiedAt"),
                value: formatDateTime(app.modifiedAt),
              },
              { label: t("common.modifiedBy"), value: app.modifiedByName },
            ]}
          />
        </>
      )}

      <Tabs defaultValue="users">
        <TabsList>
          <TabsTrigger value="users">{t("nav.users")}</TabsTrigger>
          <TabsTrigger value="organizations">
            {t("nav.organizations")}
          </TabsTrigger>
          <TabsTrigger value="roles">{t("nav.roles")}</TabsTrigger>
          <TabsTrigger value="permissions">{t("nav.permissions")}</TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="mt-4">
          <ApplicationUsersTab appId={appId} />
        </TabsContent>
        <TabsContent value="organizations" className="mt-4">
          <ApplicationOrganizationsTab appId={appId} />
        </TabsContent>
        <TabsContent value="roles" className="mt-4">
          <ApplicationRolesTab appId={appId} />
        </TabsContent>
        <TabsContent value="permissions" className="mt-4">
          <ApplicationPermissionsTab appId={appId} />
        </TabsContent>
      </Tabs>

      {app ? (
        <ApplicationEditDialog
          open={editOpen}
          onOpenChange={setEditOpen}
          application={app}
        />
      ) : null}
    </div>
  )
}
