import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import {
  KeyRound,
  LockKeyhole,
  LockKeyholeOpen,
  Mail,
  MailCheck,
  Pencil,
  ShieldCheck,
  Trash2,
  UserCheck,
  UserX,
} from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { toast } from "sonner"

import { AvatarMenu } from "@authsystem/ui/common/avatar-menu"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { DetailList } from "@authsystem/ui/common/detail-list"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import {
  PageActionSurface,
  type PageAction,
} from "@authsystem/ui/common/page-action-surface"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { api } from "@authsystem/api/client"
import { toSortParams, unwrap, toNumber } from "@authsystem/api/helpers"
import { useProfileImage } from "@authsystem/api/use-profile-image"
import { useAuth } from "@authsystem/auth/auth-context"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import {
  applicationHref,
  organizationHref,
  permissionHref,
  roleHref,
} from "@/lib/record-hrefs"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime, fullName, userStatusMeta } from "@authsystem/ui/format"
import {
  stringArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { useTabParam } from "@authsystem/ui/hooks/use-tab-param"
import type { Schemas } from "@authsystem/api/types"
import { VerifyEmailDialog } from "@authsystem/ui/common/verify-email-dialog"
import { useUserActions } from "./use-user-actions"
import { UserFormDialog } from "./user-form-dialog"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

type UserDto = Schemas["UserDto"]

type UserAuditUrlFilters = {
  entityTypes: string[]
  applications: string[]
}

const USER_AUDIT_URL_OPTIONS = {
  namespace: "audit",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.userAuditLog,
  defaultSorting: [{ id: "timestamp", desc: true }],
  filters: {
    entityTypes: stringArrayUrlFilter({ param: "entityType" }),
    applications: stringArrayUrlFilter({ param: "application" }),
  },
} satisfies ListUrlStateOptions<UserAuditUrlFilters>

const USER_DETAIL_TABS = [
  "organizations",
  "applications",
  "roles",
  "permissions",
] as const
const USER_DETAIL_TABS_WITH_AUDIT = [...USER_DETAIL_TABS, "audit"] as const

function UserOrganizationsTab({ userId }: { userId: string }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["users", userId, "organizations"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/organizations", {
          params: { path: { id: userId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["OrganizationSummaryDto"], unknown>[] = [
    avatarColumn<Schemas["OrganizationSummaryDto"]>({
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
          href={organizationHref(row.original.id)}
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
      id: "userRole",
      accessorFn: (row) => row.userRole ?? "",
      filterFn: "faceted",
      header: t("common.role"),
      meta: { label: t("common.role"), filterVariant: "faceted" },
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
      tableId="user-orgs"
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

function UserApplicationsTab({ userId }: { userId: string }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["users", userId, "applications"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/applications", {
          params: { path: { id: userId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["UserApplicationDto"], unknown>[] = [
    avatarColumn<Schemas["UserApplicationDto"]>({
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
          href={applicationHref(row.original.applicationId)}
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
      id: "accessSource",
      accessorFn: (row) => row.accessSource ?? "",
      filterFn: "faceted",
      header: t("users.accessSource"),
      meta: { label: t("users.accessSource"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <Badge variant="outline">
          {t(`users.access.${row.original.accessSource ?? "direct"}`)}
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
      tableId="user-apps"
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

function UserRolesTab({ userId }: { userId: string }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["users", userId, "roles"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/roles", {
          params: { path: { id: userId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["UserRoleDto"], unknown>[] = [
    {
      id: "roleName",
      accessorFn: (row) => row.roleName ?? "",
      header: t("common.role"),
      meta: { label: t("common.role"), covers: ["roleCode"] },
      cell: ({ row }) => (
        <RecordLink
          href={roleHref(row.original.roleId)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">{row.original.roleName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.roleCode}
          </p>
        </RecordLink>
      ),
    },
    {
      id: "applicationName",
      accessorFn: (row) => row.applicationName ?? "",
      filterFn: "faceted",
      header: t("nav.applications"),
      meta: { label: t("nav.applications"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.applicationName ?? "—"}
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
    {
      id: "expiresAt",
      accessorFn: (row) => row.expiresAt ?? "",
      header: t("common.expiresAt"),
      meta: { label: t("common.expiresAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.expiresAt)}
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
  ]

  return (
    <DataTable
      tableId="user-roles"
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

function UserPermissionsTab({ userId }: { userId: string }) {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["users", userId, "permissions"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/permissions", {
          params: { path: { id: userId } },
        })
      ),
  })

  const columns: ColumnDef<Schemas["UserPermissionDto"], unknown>[] = [
    {
      id: "permissionName",
      accessorFn: (row) => row.permissionName ?? "",
      header: t("nav.permissions"),
      meta: { label: t("nav.permissions"), covers: ["permissionCode"] },
      cell: ({ row }) => (
        <RecordLink
          href={permissionHref(row.original.permissionId)}
          className="min-w-0 text-start"
        >
          <p className="truncate font-medium">{row.original.permissionName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.permissionCode}
          </p>
        </RecordLink>
      ),
    },
    {
      id: "applicationName",
      accessorFn: (row) => row.applicationName ?? "",
      filterFn: "faceted",
      header: t("nav.applications"),
      meta: { label: t("nav.applications"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.applicationName ?? "—"}
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
    {
      id: "expiresAt",
      accessorFn: (row) => row.expiresAt ?? "",
      header: t("common.expiresAt"),
      meta: { label: t("common.expiresAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.expiresAt)}
        </span>
      ),
    },
  ]

  return (
    <DataTable
      tableId="user-perms"
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

function UserAuditLogsTab({ userId }: { userId: string }) {
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
  } = useListUrlState(USER_AUDIT_URL_OPTIONS)
  const { sortBy, sortDirection } = toSortParams(sorting)
  const columnFilters: ColumnFiltersState = [
    ...(filters.entityTypes.length
      ? [{ id: "entityType", value: filters.entityTypes }]
      : []),
    ...(filters.applications.length
      ? [{ id: "applicationName", value: filters.applications }]
      : []),
  ]
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      entityTypes:
        (next.find((filter) => filter.id === "entityType")?.value as
          | string[]
          | undefined) ?? [],
      applications:
        (next.find((filter) => filter.id === "applicationName")?.value as
          | string[]
          | undefined) ?? [],
    })

  const query = useQuery({
    queryKey: ["audit-logs", { userId, page, pageSize, sortBy, sortDirection }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              userId,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  const columns: ColumnDef<Schemas["AuditLogDto"], unknown>[] = [
    {
      id: "action",
      accessorFn: (row) => row.action ?? "",
      header: t("auditLogs.action"),
      meta: { label: t("auditLogs.action") },
      cell: ({ row }) => (
        <span className="font-medium">{row.original.action}</span>
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
      id: "applicationName",
      accessorFn: (row) => row.applicationName ?? "",
      filterFn: "faceted",
      header: t("nav.applications"),
      meta: { label: t("nav.applications"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.applicationName ?? "—"}
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
  ]

  return (
    <DataTable
      tableId="user-audit"
      columns={columns}
      data={query.data?.logs ?? []}
      isLoading={query.isLoading}
      error={query.isError ? query.error : undefined}
      onRetry={() => query.refetch()}
      enableExport={false}
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

export function UserDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const userId = id as string
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { hasPermission } = useAuth()

  const canUpdate = hasPermission(PERMISSIONS.users.update)
  const profileImage = useProfileImage(userId)
  const canDelete = hasPermission(PERMISSIONS.users.delete)
  const canManageRoles = hasPermission(PERMISSIONS.users.manageRoles)
  const canManagePerms = hasPermission(PERMISSIONS.users.managePermissions)
  const canManage = hasPermission(PERMISSIONS.users.manage)
  const canReadAudit = hasPermission(PERMISSIONS.auditLogs.read)
  const [activeTab, setActiveTab] = useTabParam(
    canReadAudit ? USER_DETAIL_TABS_WITH_AUDIT : USER_DETAIL_TABS
  )

  const [formOpen, setFormOpen] = React.useState(false)
  const [rolesOpen, setRolesOpen] = React.useState(false)
  const [permsOpen, setPermsOpen] = React.useState(false)
  const [lockOpen, setLockOpen] = React.useState(false)
  const [lockReason, setLockReason] = React.useState("")
  const [deleteOpen, setDeleteOpen] = React.useState(false)
  const [verifyEmailOpen, setVerifyEmailOpen] = React.useState(false)

  const detailQuery = useQuery({
    queryKey: ["users", userId],
    enabled: Boolean(userId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}", { params: { path: { id: userId } } })
      ),
  })
  const user: UserDto | undefined = detailQuery.data

  const { statusAction, deleteMutation } = useUserActions({
    onStatusChanged: () => {
      setLockOpen(false)
      setLockReason("")
    },
    onDeleted: () => {
      setDeleteOpen(false)
      void navigate("/users")
    },
  })

  const sendPasswordReset = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/Auth/forgot-password", {
        body: { email: user?.email ?? "" },
      })
      if (error) throw error
    },
    onSuccess: () => toast.success(t("users.passwordResetSent")),
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const statusKey = userStatusMeta(user?.status).key
  const isLocked = statusKey === "locked"
  const isInactive = statusKey === "inactive"
  const displayName =
    user?.displayName ||
    fullName(user?.firstName, user?.lastName, user?.email ?? "")
  const userActions: PageAction[] = [
    ...(canUpdate
      ? [
          {
            id: "edit",
            label: t("common.edit"),
            icon: Pencil,
            variant: "default" as const,
            onAction: () => setFormOpen(true),
          },
        ]
      : []),
    ...(canManageRoles
      ? [
          {
            id: "roles",
            label: t("users.manageRoles"),
            icon: ShieldCheck,
            onAction: () => setRolesOpen(true),
          },
        ]
      : []),
    ...(canManagePerms
      ? [
          {
            id: "permissions",
            label: t("users.managePermissions"),
            icon: KeyRound,
            onAction: () => setPermsOpen(true),
          },
        ]
      : []),
    ...(canManage
      ? [
          {
            id: "password-reset",
            label: t("users.sendPasswordReset"),
            icon: Mail,
            pending: sendPasswordReset.isPending,
            onAction: () => sendPasswordReset.mutate(),
          },
          ...(!user?.emailConfirmed
            ? [
                {
                  id: "verify-email",
                  label: t("users.resendConfirmation"),
                  icon: MailCheck,
                  onAction: () => setVerifyEmailOpen(true),
                },
              ]
            : []),
          isLocked
            ? {
                id: "unlock",
                label: t("users.unlock"),
                icon: LockKeyholeOpen,
                pending: statusAction.isPending,
                onAction: () =>
                  statusAction.mutate({ id: userId, action: "unlock" }),
              }
            : {
                id: "lock",
                label: t("users.lock"),
                icon: LockKeyhole,
                pending: statusAction.isPending,
                onAction: () => setLockOpen(true),
              },
          isInactive
            ? {
                id: "activate",
                label: t("users.activate"),
                icon: UserCheck,
                pending: statusAction.isPending,
                onAction: () =>
                  statusAction.mutate({ id: userId, action: "activate" }),
              }
            : {
                id: "deactivate",
                label: t("users.deactivate"),
                icon: UserX,
                pending: statusAction.isPending,
                onAction: () =>
                  statusAction.mutate({ id: userId, action: "deactivate" }),
              },
        ]
      : []),
    ...(canDelete
      ? [
          {
            id: "delete",
            label: t("common.delete"),
            icon: Trash2,
            variant: "destructive" as const,
            pending: deleteMutation.isPending,
            onAction: () => setDeleteOpen(true),
          },
        ]
      : []),
  ]

  usePageBreadcrumb(user ? displayName : undefined)

  return (
    <div className="flex flex-col gap-6">
      {detailQuery.isLoading || !user ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={displayName}
            description={user.email}
            leading={
              canUpdate ? (
                <AvatarMenu
                  src={user.profileImageUrl}
                  name={displayName}
                  size="xl"
                  onChange={profileImage.onChange}
                  onRemove={profileImage.onRemove}
                  pending={profileImage.pending}
                />
              ) : (
                <EntityAvatar
                  src={user.profileImageUrl}
                  name={displayName}
                  size="xl"
                />
              )
            }
            actions={
              <PageActionSurface
                actions={userActions}
                label={t("common.actions")}
              />
            }
          />
          <DetailList
            items={[
              {
                label: t("common.status"),
                value: (
                  <Badge variant={userStatusMeta(user.status).variant}>
                    {t(`common.${userStatusMeta(user.status).key}`)}
                  </Badge>
                ),
              },
              {
                label: t("users.emailConfirmed"),
                value: user.emailConfirmed ? t("common.yes") : t("common.no"),
              },
              {
                label: t("users.phoneConfirmed"),
                value: user.phoneConfirmed ? t("common.yes") : t("common.no"),
              },
              {
                label: t("users.twoFactor"),
                value: user.twoFactorEnabled
                  ? t("common.enabled")
                  : t("common.disabled"),
              },
              { label: t("users.phoneNumber"), value: user.phoneNumber },
              {
                label: t("users.preferredLanguage"),
                value: user.preferredLanguage,
              },
              { label: t("users.timeZone"), value: user.timeZone },
              {
                label: t("users.lastLogin"),
                value: formatDateTime(user.lastLoginAt),
              },
              { label: t("users.lastLoginIp"), value: user.lastLoginIp },
              {
                label: t("users.failedLoginAttempts"),
                value: toNumber(user.failedLoginAttempts),
              },
              {
                label: t("users.lockoutEnd"),
                value: formatDateTime(user.lockoutEnd),
              },
              {
                label: t("users.passwordChangedAt"),
                value: formatDateTime(user.passwordChangedAt),
              },
              {
                label: t("users.passwordExpires"),
                value: formatDateTime(user.passwordExpiresUtc),
              },
              {
                label: t("users.mustChangePassword"),
                value: user.mustChangePassword
                  ? t("common.yes")
                  : t("common.no"),
              },
              {
                label: t("common.createdAt"),
                value: formatDateTime(user.createdAt),
              },
              { label: t("common.createdBy"), value: user.createdByName },
              {
                label: t("common.modifiedAt"),
                value: formatDateTime(user.modifiedAt),
              },
              { label: t("common.modifiedBy"), value: user.modifiedByName },
            ]}
          />
        </>
      )}

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList>
          <TabsTrigger value="organizations">
            {t("nav.organizations")}
          </TabsTrigger>
          <TabsTrigger value="applications">
            {t("nav.applications")}
          </TabsTrigger>
          <TabsTrigger value="roles">{t("nav.roles")}</TabsTrigger>
          <TabsTrigger value="permissions">{t("nav.permissions")}</TabsTrigger>
          {canReadAudit ? (
            <TabsTrigger value="audit">{t("nav.auditLogs")}</TabsTrigger>
          ) : null}
        </TabsList>
        <TabsContent value="organizations" className="mt-4">
          <UserOrganizationsTab userId={userId} />
        </TabsContent>
        <TabsContent value="applications" className="mt-4">
          <UserApplicationsTab userId={userId} />
        </TabsContent>
        <TabsContent value="roles" className="mt-4">
          <UserRolesTab userId={userId} />
        </TabsContent>
        <TabsContent value="permissions" className="mt-4">
          <UserPermissionsTab userId={userId} />
        </TabsContent>
        {canReadAudit ? (
          <TabsContent value="audit" className="mt-4">
            <UserAuditLogsTab userId={userId} />
          </TabsContent>
        ) : null}
      </Tabs>

      {user ? (
        <UserFormDialog
          open={formOpen}
          onOpenChange={setFormOpen}
          user={user}
        />
      ) : null}
      {user && rolesOpen ? (
        <UserRolesDialog
          open={rolesOpen}
          onOpenChange={setRolesOpen}
          user={user}
        />
      ) : null}
      {user && permsOpen ? (
        <UserPermissionsDialog
          open={permsOpen}
          onOpenChange={setPermsOpen}
          user={user}
        />
      ) : null}
      {user && !user.emailConfirmed ? (
        <VerifyEmailDialog
          open={verifyEmailOpen}
          onOpenChange={setVerifyEmailOpen}
          userId={userId}
          email={user.email ?? ""}
          onVerified={() =>
            void queryClient.invalidateQueries({ queryKey: ["users"] })
          }
        />
      ) : null}

      <ConfirmDialog
        open={lockOpen}
        onOpenChange={(open) => {
          setLockOpen(open)
          if (!open) setLockReason("")
        }}
        title={t("users.lockTitle")}
        confirmLabel={t("users.lock")}
        destructive
        loading={statusAction.isPending}
        onConfirm={() =>
          statusAction.mutate({
            id: userId,
            action: "lock",
            reason: lockReason,
          })
        }
      >
        <Field>
          <FieldLabel htmlFor="lock-reason">{t("users.lockReason")}</FieldLabel>
          <Input
            id="lock-reason"
            value={lockReason}
            onChange={(e) => setLockReason(e.target.value)}
            placeholder={t("users.lockReasonPlaceholder")}
          />
        </Field>
      </ConfirmDialog>

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title={t("users.deleteTitle")}
        description={t("users.deleteBody", { name: displayName })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate(userId)}
      />
    </div>
  )
}
