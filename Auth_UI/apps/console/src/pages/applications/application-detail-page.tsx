import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { DetailList } from "@authsystem/ui/common/detail-list"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { LogoAvatar } from "@authsystem/ui/common/logo-avatar"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Switch } from "@authsystem/ui/switch"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
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
import {
  organizationHref,
  permissionHref,
  roleHref,
  userHref,
} from "@/lib/record-hrefs"
import {
  accessMode,
  formatDateTime,
  fullName,
  userStatusMeta,
} from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import {
  enumArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { useTabParam } from "@authsystem/ui/hooks/use-tab-param"
import type { Schemas } from "@authsystem/api/types"
import { ApplicationAccessDialog } from "./application-access-dialog"
import { ApplicationEditDialog } from "./application-dialogs"

type ApplicationUsersUrlFilters = {
  accessSource: Array<"grant" | "direct" | "organization" | "multiple">
  status: Array<"active" | "inactive" | "locked" | "pending">
}

const APPLICATION_USERS_URL_OPTIONS = {
  namespace: "users",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.applicationUsers,
  defaultSorting: [{ id: "email", desc: false }],
  filters: {
    accessSource: enumArrayUrlFilter(
      ["grant", "direct", "organization", "multiple"],
      "access"
    ),
    status: enumArrayUrlFilter(["active", "inactive", "locked", "pending"]),
  },
} satisfies ListUrlStateOptions<ApplicationUsersUrlFilters>

type ApplicationOrganizationsUrlFilters = {
  status: Array<"active" | "inactive">
}

const APPLICATION_ORGANIZATIONS_URL_OPTIONS = {
  namespace: "organizations",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.applicationOrganizations,
  defaultSorting: [{ id: "name", desc: false }],
  filters: {
    status: enumArrayUrlFilter(["active", "inactive"]),
  },
} satisfies ListUrlStateOptions<ApplicationOrganizationsUrlFilters>

const APPLICATION_DETAIL_TABS = [
  "users",
  "organizations",
  "roles",
  "permissions",
] as const

function ApplicationUsersTab({ appId }: { appId: string }) {
  const { t } = useTranslation()

  /**
   * Why a user is on this roster. Not the same question as "can they sign in":
   * only an invitation admits anyone to a restricted application, and an open
   * one admits people who never appear here at all.
   */
  const accessSourceLabel = (source: string | undefined) => {
    switch (source) {
      case "grant":
        return t("applications.accessViaGrant")
      case "direct":
        return t("applications.accessViaDirect")
      case "organization":
        return t("applications.accessViaOrganization")
      default:
        return t("applications.accessViaMultiple")
    }
  }

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
  } = useListUrlState(APPLICATION_USERS_URL_OPTIONS)
  const search = useDebouncedValue(searchInput)
  const { sortBy, sortDirection } = toSortParams(sorting)
  const columnFilters: ColumnFiltersState = [
    ...(filters.accessSource.length
      ? [{ id: "accessSource", value: filters.accessSource }]
      : []),
    ...(filters.status.length ? [{ id: "status", value: filters.status }] : []),
  ]
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      accessSource:
        (next.find((filter) => filter.id === "accessSource")?.value as
          | ApplicationUsersUrlFilters["accessSource"]
          | undefined) ?? [],
      status:
        (next.find((filter) => filter.id === "status")?.value as
          | ApplicationUsersUrlFilters["status"]
          | undefined) ?? [],
    })

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
      collectAllPages<Schemas["ApplicationUserDto"]>(
        async (pageNumber, size) => {
          const result = await unwrap(
            api.GET("/api/v1/Applications/{id}/users", {
              params: {
                path: { id: appId },
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
    [appId, search, sortBy, sortDirection]
  )

  const columns: ColumnDef<Schemas["ApplicationUserDto"], unknown>[] = [
    avatarColumn<Schemas["ApplicationUserDto"]>({
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
      // `fullName` too: the cell composes that exact string out of the two name
      // fields, so its auto column was the same name a second time.
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
      id: "accessSource",
      enableSorting: false,
      accessorFn: (row) => row.accessSource ?? "multiple",
      filterFn: "faceted",
      header: t("applications.accessVia"),
      meta: {
        label: t("applications.accessVia"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "grant", label: t("applications.accessViaGrant") },
          { value: "direct", label: t("applications.accessViaDirect") },
          {
            value: "organization",
            label: t("applications.accessViaOrganization"),
          },
          { value: "multiple", label: t("applications.accessViaMultiple") },
        ],
      },
      cell: ({ row }) => (
        <Badge variant="outline">
          {accessSourceLabel(row.original.accessSource)}
        </Badge>
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
        onChange={setSearchInput}
        placeholder={t("users.searchPlaceholder")}
      />
      <DataTable
        tableId="app-users"
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

function ApplicationOrganizationsTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const {
    pageIndex: page,
    pageSize,
    sorting,
    filters,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilters,
  } = useListUrlState(APPLICATION_ORGANIZATIONS_URL_OPTIONS)
  const { sortBy, sortDirection } = toSortParams(sorting)
  const columnFilters: ColumnFiltersState = filters.status.length
    ? [{ id: "isActive", value: filters.status }]
    : []
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      status:
        (next.find((filter) => filter.id === "isActive")?.value as
          | ApplicationOrganizationsUrlFilters["status"]
          | undefined) ?? [],
    })

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
      covers: ["logoUrl"],
    }),
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name"), covers: ["code"] },
      cell: ({ row }) => (
        <RecordLink
          href={organizationHref(row.original.organizationId)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </RecordLink>
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
  )
}

function ApplicationRolesTab({ appId }: { appId: string }) {
  const { t } = useTranslation()

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
      meta: { label: t("common.name"), covers: ["code"] },
      cell: ({ row }) => (
        <RecordLink
          href={roleHref(row.original.id)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </RecordLink>
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
        covers: ["isActive"],
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
      meta: { label: t("common.name"), covers: ["code"] },
      cell: ({ row }) => (
        <RecordLink
          href={permissionHref(row.original.id)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </RecordLink>
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
        covers: ["isActive"],
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
  const [activeTab, setActiveTab] = useTabParam(APPLICATION_DETAIL_TABS)
  const [editOpen, setEditOpen] = React.useState(false)
  const [accessOpen, setAccessOpen] = React.useState(false)
  const [deactivateOpen, setDeactivateOpen] = React.useState(false)

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

  const activeMutation = useMutation({
    mutationFn: async (isActive: boolean) => {
      const { error } = await api.POST(
        isActive
          ? "/api/v1/Applications/{id}/activate"
          : "/api/v1/Applications/{id}/deactivate",
        { params: { path: { id: appId } } }
      )
      if (error) throw error
      return isActive
    },
    onSuccess: (isActive) => {
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      toast.success(
        isActive ? t("applications.activated") : t("applications.deactivated")
      )
      setDeactivateOpen(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <div className="flex flex-col gap-6">
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
                  const { error } = await api.PUT("/api/v1/Applications/{id}", {
                    params: { path: { id: appId } },
                    // A full replace: every setting the update contract
                    // accepts has to be resent, or changing the logo quietly
                    // resets it. `redirectUris` is the one exception — the
                    // API reads null as "leave the allowlist alone".
                    body: {
                      name: app.name ?? "",
                      description: app.description ?? null,
                      baseUrl: app.baseUrl ?? null,
                      logoUrl: logoKey,
                      contactEmail: app.contactEmail ?? null,
                      // Without this, uploading a logo would send the
                      // contract's default and silently close an open
                      // application down to its invitation list.
                      accessMode: accessMode(
                        app.accessMode
                      ) as unknown as number,
                      allowSelfRegistration: app.allowSelfRegistration ?? false,
                      requireTwoFactor: app.requireTwoFactor ?? false,
                      requireEmailVerification:
                        app.requireEmailVerification ?? false,
                      sessionTimeoutMinutes: app.sessionTimeoutMinutes ?? 60,
                      maxConcurrentSessions: app.maxConcurrentSessions ?? 5,
                      reauthenticationMaxAgeMinutes:
                        app.reauthenticationMaxAgeMinutes ?? null,
                    },
                  })
                  if (error) throw error
                }}
              />
            }
            actions={
              canUpdate ? (
                <div className="flex items-center gap-3">
                  <Field orientation="horizontal" className="w-auto">
                    <FieldLabel
                      htmlFor="application-available"
                      className="font-normal whitespace-nowrap"
                    >
                      {t("applications.available")}
                    </FieldLabel>
                    <Switch
                      id="application-available"
                      checked={app.isActive ?? false}
                      disabled={activeMutation.isPending}
                      onCheckedChange={(next) => {
                        // Turning it off locks everyone out at once, so it is
                        // confirmed; turning it back on is not.
                        if (next) activeMutation.mutate(true)
                        else setDeactivateOpen(true)
                      }}
                    />
                  </Field>
                  <Button variant="outline" onClick={() => setEditOpen(true)}>
                    {t("common.edit")}
                  </Button>
                </div>
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
                label: t("applications.available"),
                value: (
                  <Badge variant={app.isActive ? "default" : "secondary"}>
                    {app.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                ),
              },
              {
                label: t("applications.accessMode"),
                value: (
                  <Badge
                    variant={
                      accessMode(app.accessMode) === "Everyone"
                        ? "default"
                        : "secondary"
                    }
                  >
                    {accessMode(app.accessMode) === "Everyone"
                      ? t("applications.accessModeEveryone")
                      : t("applications.accessModeRestricted")}
                  </Badge>
                ),
              },
              { label: t("applications.baseUrl"), value: app.baseUrl },
              {
                label: t("applications.contactEmail"),
                value: app.contactEmail,
              },
              // allowSelfRegistration is deliberately not shown: no registration
              // path consults the column, so displaying it stated a policy that
              // was never applied. The enforced one is
              // Registration:AllowSelfRegistration under System settings, and it
              // governs the whole server rather than one application.
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
              // maxConcurrentSessions is deliberately not shown: no sign-in path
              // consults the column, so displaying it stated a limit that was
              // never applied. The enforced one is Session:MaxConcurrentSessions
              // under System settings, counted per user across all applications.
              {
                // Unset means step-up is off; DetailList drops empty values, so
                // the row only appears when a threshold is actually configured.
                label: t("applications.reauthMaxAge"),
                value:
                  app.reauthenticationMaxAgeMinutes != null
                    ? toNumber(app.reauthenticationMaxAgeMinutes)
                    : null,
              },
              {
                label: t("applications.redirectUris"),
                value: app.redirectUris?.length ? (
                  // `items-start` rather than `text-start`: alignment must come
                  // from this column, which runs in the page's direction, not
                  // from the URI itself. A `text-start` inside `dir="ltr"`
                  // resolves against THAT element — it means "left" even on an
                  // Arabic page, which is how the list ended up detached at the
                  // far side of its own row.
                  //
                  // `max-w-full` is what keeps that safe. `items-start` sizes
                  // each URI to fit-content, and CSS Text excludes the breaks
                  // `break-words` introduces from min-content — so a long URI
                  // keeps its full unwrapped width and the Card's
                  // `overflow-hidden` cuts the overhang off with no scrollbar.
                  // In RTL the clipped side is the START of the URL: the scheme
                  // and host disappear. The cap restores wrapping without
                  // touching the alignment.
                  <div className="flex flex-col items-start">
                    {app.redirectUris.map((uri) => (
                      <span key={uri} dir="ltr" className="max-w-full">
                        {uri}
                      </span>
                    ))}
                  </div>
                ) : null,
                fullWidth: true,
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

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList>
          <TabsTrigger value="users">{t("nav.users")}</TabsTrigger>
          <TabsTrigger value="organizations">
            {t("nav.organizations")}
          </TabsTrigger>
          <TabsTrigger value="roles">{t("nav.roles")}</TabsTrigger>
          <TabsTrigger value="permissions">{t("nav.permissions")}</TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="mt-4">
          <div className="flex flex-col gap-4">
            {app && accessMode(app.accessMode) === "Everyone" ? (
              <Alert>
                <AlertDescription>
                  {t("applications.openToEveryoneNotice")}
                </AlertDescription>
              </Alert>
            ) : null}
            {canUpdate ? (
              <div className="flex justify-end">
                <Button variant="outline" onClick={() => setAccessOpen(true)}>
                  {t("applications.manageAccess")}
                </Button>
              </div>
            ) : null}
            <ApplicationUsersTab appId={appId} />
          </div>
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
        <>
          <ApplicationEditDialog
            open={editOpen}
            onOpenChange={setEditOpen}
            application={app}
          />
          <ApplicationAccessDialog
            open={accessOpen}
            onOpenChange={setAccessOpen}
            applicationId={appId}
            applicationName={app.name ?? ""}
          />
          <ConfirmDialog
            open={deactivateOpen}
            onOpenChange={setDeactivateOpen}
            title={t("applications.deactivateTitle")}
            description={t("applications.deactivateBody", {
              name: app.name ?? "",
            })}
            confirmLabel={t("common.confirm")}
            destructive
            loading={activeMutation.isPending}
            onConfirm={() => activeMutation.mutate(false)}
          />
        </>
      ) : null}
    </div>
  )
}
