import { useMutation, useQuery } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { ArrowLeft, ChevronDown } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate, useParams } from "react-router-dom"
import { toast } from "sonner"

import { ConfirmDialog } from "@/components/common/confirm-dialog"
import { DetailList } from "@/components/common/detail-list"
import { PageHeader } from "@/components/common/page-header"
import { DataTable } from "@/components/data-table/data-table"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Skeleton } from "@/components/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { api } from "@/lib/api/client"
import { toSortParams, unwrap, toNumber } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import { formatDate, formatDateTime, fullName, userStatusMeta } from "@/lib/format"
import type { Schemas } from "@/lib/api/types"
import { useUserActions } from "./use-user-actions"
import { UserFormDialog } from "./user-form-dialog"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

type UserDto = Schemas["UserDto"]

function UserOrganizationsTab({ userId }: { userId: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()

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
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/organizations/${row.original.id}`)}
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </button>
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
  const navigate = useNavigate()

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
  const navigate = useNavigate()

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
      meta: { label: t("common.role") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/roles/${row.original.roleId}`)}
        >
          <p className="truncate font-medium">{row.original.roleName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.roleCode}
          </p>
        </button>
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
          {formatDate(row.original.expiresAt)}
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
          {formatDate(row.original.createdAt)}
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
  const navigate = useNavigate()

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
      meta: { label: t("nav.permissions") },
      cell: ({ row }) => (
        <button
          type="button"
          className="min-w-0 text-start hover:underline"
          onClick={() => navigate(`/permissions/${row.original.permissionId}`)}
        >
          <p className="truncate font-medium">{row.original.permissionName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.permissionCode}
          </p>
        </button>
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
          {formatDate(row.original.expiresAt)}
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
  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "timestamp", desc: true },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const query = useQuery({
    queryKey: [
      "audit-logs",
      { userId, page, pageSize, sortBy, sortDirection },
    ],
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

export function UserDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const userId = id as string
  const navigate = useNavigate()
  const { hasPermission } = useAuth()

  const canUpdate = hasPermission(PERMISSIONS.users.update)
  const canDelete = hasPermission(PERMISSIONS.users.delete)
  const canManageRoles = hasPermission(PERMISSIONS.users.manageRoles)
  const canManagePerms = hasPermission(PERMISSIONS.users.managePermissions)
  const canManage = hasPermission(PERMISSIONS.users.manage)
  const canReadAudit = hasPermission(PERMISSIONS.auditLogs.read)

  const [formOpen, setFormOpen] = React.useState(false)
  const [rolesOpen, setRolesOpen] = React.useState(false)
  const [permsOpen, setPermsOpen] = React.useState(false)
  const [lockOpen, setLockOpen] = React.useState(false)
  const [lockReason, setLockReason] = React.useState("")
  const [deleteOpen, setDeleteOpen] = React.useState(false)

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

  const resendConfirmation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST(
        "/api/v1/Auth/resend-verification-email",
        { body: { email: user?.email ?? "" } }
      )
      if (error) throw error
    },
    onSuccess: () => toast.success(t("users.confirmationResent")),
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const statusKey = userStatusMeta(user?.status).key
  const isLocked = statusKey === "locked"
  const isInactive = statusKey === "inactive"
  const displayName =
    user?.displayName ||
    fullName(user?.firstName, user?.lastName, user?.email ?? "")
  const hasActions =
    canManageRoles || canManagePerms || canManage || canDelete

  return (
    <div className="space-y-6">
      <Link
        to="/users"
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" />
        {t("users.title")}
      </Link>

      {detailQuery.isLoading || !user ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={displayName}
            description={user.email}
            actions={
              <>
                {canUpdate ? (
                  <Button variant="outline" onClick={() => setFormOpen(true)}>
                    {t("common.edit")}
                  </Button>
                ) : null}
                {hasActions ? (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="outline">
                        {t("common.actions")}
                        <ChevronDown />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" className="w-56">
                      {canManageRoles ? (
                        <DropdownMenuItem onClick={() => setRolesOpen(true)}>
                          {t("users.manageRoles")}
                        </DropdownMenuItem>
                      ) : null}
                      {canManagePerms ? (
                        <DropdownMenuItem onClick={() => setPermsOpen(true)}>
                          {t("users.managePermissions")}
                        </DropdownMenuItem>
                      ) : null}
                      {canManage ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            disabled={sendPasswordReset.isPending}
                            onClick={() => sendPasswordReset.mutate()}
                          >
                            {t("users.sendPasswordReset")}
                          </DropdownMenuItem>
                          {!user.emailConfirmed ? (
                            <DropdownMenuItem
                              disabled={resendConfirmation.isPending}
                              onClick={() => resendConfirmation.mutate()}
                            >
                              {t("users.resendConfirmation")}
                            </DropdownMenuItem>
                          ) : null}
                          <DropdownMenuSeparator />
                          {isLocked ? (
                            <DropdownMenuItem
                              onClick={() =>
                                statusAction.mutate({
                                  id: userId,
                                  action: "unlock",
                                })
                              }
                            >
                              {t("users.unlock")}
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem onClick={() => setLockOpen(true)}>
                              {t("users.lock")}
                            </DropdownMenuItem>
                          )}
                          {isInactive ? (
                            <DropdownMenuItem
                              onClick={() =>
                                statusAction.mutate({
                                  id: userId,
                                  action: "activate",
                                })
                              }
                            >
                              {t("users.activate")}
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem
                              onClick={() =>
                                statusAction.mutate({
                                  id: userId,
                                  action: "deactivate",
                                })
                              }
                            >
                              {t("users.deactivate")}
                            </DropdownMenuItem>
                          )}
                        </>
                      ) : null}
                      {canDelete ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleteOpen(true)}
                          >
                            {t("common.delete")}
                          </DropdownMenuItem>
                        </>
                      ) : null}
                    </DropdownMenuContent>
                  </DropdownMenu>
                ) : null}
              </>
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
                value: user.emailConfirmed
                  ? t("common.yes")
                  : t("common.no"),
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
              {
                label: t("common.createdAt"),
                value: formatDate(user.createdAt),
              },
              {
                label: t("common.modifiedAt"),
                value: formatDate(user.modifiedAt),
              },
            ]}
          />
        </>
      )}

      <Tabs defaultValue="organizations">
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
        <UserFormDialog open={formOpen} onOpenChange={setFormOpen} user={user} />
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
          statusAction.mutate({ id: userId, action: "lock", reason: lockReason })
        }
      >
        <div className="space-y-2">
          <Label htmlFor="lock-reason">{t("users.lockReason")}</Label>
          <Input
            id="lock-reason"
            value={lockReason}
            onChange={(e) => setLockReason(e.target.value)}
          />
        </div>
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
