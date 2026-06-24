import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus, Search } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@/components/common/confirm-dialog"
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
import { api } from "@/lib/api/client"
import { unwrap, toNumber } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import { formatDate, fullName, userStatusMeta } from "@/lib/format"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import type { Schemas } from "@/lib/api/types"
import { UserFormDialog } from "./user-form-dialog"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

type UserDto = Schemas["UserDto"]

export function UsersPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)

  const [formOpen, setFormOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<UserDto | undefined>()
  const [rolesUser, setRolesUser] = React.useState<UserDto | undefined>()
  const [permsUser, setPermsUser] = React.useState<UserDto | undefined>()
  const [lockUser, setLockUser] = React.useState<UserDto | undefined>()
  const [lockReason, setLockReason] = React.useState("")
  const [deleteUser, setDeleteUser] = React.useState<UserDto | undefined>()

  const canCreate = hasPermission(PERMISSIONS.users.create)
  const canUpdate = hasPermission(PERMISSIONS.users.update)
  const canDelete = hasPermission(PERMISSIONS.users.delete)
  const canManageRoles = hasPermission(PERMISSIONS.users.manageRoles)
  const canManagePerms = hasPermission(PERMISSIONS.users.managePermissions)
  const canManage = hasPermission(PERMISSIONS.users.manage)

  const query = useQuery({
    queryKey: ["users", { page, pageSize, search }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              searchTerm: search || undefined,
            },
          },
        })
      ),
  })

  const invalidateUsers = () =>
    queryClient.invalidateQueries({ queryKey: ["users"] })

  const statusAction = useMutation({
    mutationFn: async (input: {
      id: string
      action: "lock" | "unlock" | "activate" | "deactivate"
      reason?: string
    }): Promise<string> => {
      const path = { id: input.id }
      switch (input.action) {
        case "lock": {
          const { error } = await api.POST("/api/v1/Users/{id}/lock", {
            params: { path },
            body: { reason: input.reason ?? "" },
          })
          if (error) throw error
          return "users.locked"
        }
        case "unlock": {
          const { error } = await api.POST("/api/v1/Users/{id}/unlock", {
            params: { path },
          })
          if (error) throw error
          return "users.unlocked"
        }
        case "activate": {
          const { error } = await api.POST("/api/v1/Users/{id}/activate", {
            params: { path },
          })
          if (error) throw error
          return "users.activated"
        }
        case "deactivate": {
          const { error } = await api.POST("/api/v1/Users/{id}/deactivate", {
            params: { path },
          })
          if (error) throw error
          return "users.deactivated"
        }
      }
    },
    onSuccess: (successKey) => {
      void invalidateUsers()
      toast.success(t(successKey))
      setLockUser(undefined)
      setLockReason("")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Users/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidateUsers()
      toast.success(t("users.deleted"))
      setDeleteUser(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const hasRowActions =
    canUpdate || canDelete || canManageRoles || canManagePerms || canManage

  const columns: ColumnDef<UserDto, unknown>[] = [
    {
      header: t("common.name"),
      cell: ({ row }) => {
        const user = row.original
        return (
          <div className="min-w-0">
            <p className="truncate font-medium">
              {user.displayName ||
                fullName(user.firstName, user.lastName, user.email ?? "")}
            </p>
          </div>
        )
      },
    },
    { header: t("common.email"), accessorKey: "email" },
    {
      header: t("common.status"),
      cell: ({ row }) => {
        const meta = userStatusMeta(row.original.status)
        return <Badge variant={meta.variant}>{t(`common.${meta.key}`)}</Badge>
      },
    },
    {
      header: t("users.roles"),
      cell: ({ row }) => {
        const roles = row.original.roles ?? []
        return roles.length > 0 ? (
          <span className="text-sm text-muted-foreground">{roles.length}</span>
        ) : (
          <span className="text-sm text-muted-foreground">—</span>
        )
      },
    },
    {
      header: t("users.lastLogin"),
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.original.lastLoginAt)}
        </span>
      ),
    },
    {
      header: t("common.createdAt"),
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.original.createdAt)}
        </span>
      ),
    },
    ...(hasRowActions
      ? [
          {
            id: "actions",
            header: () => (
              <span className="sr-only">{t("common.actions")}</span>
            ),
            cell: ({ row }) => {
              const user = row.original
              const isLocked = userStatusMeta(user.status).key === "locked"
              return (
                <div className="text-end">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={t("common.actions")}
                      >
                        <MoreHorizontal />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" className="w-48">
                      {canUpdate ? (
                        <DropdownMenuItem
                          onClick={() => {
                            setEditing(user)
                            setFormOpen(true)
                          }}
                        >
                          {t("common.edit")}
                        </DropdownMenuItem>
                      ) : null}
                      {canManageRoles ? (
                        <DropdownMenuItem onClick={() => setRolesUser(user)}>
                          {t("users.manageRoles")}
                        </DropdownMenuItem>
                      ) : null}
                      {canManagePerms ? (
                        <DropdownMenuItem onClick={() => setPermsUser(user)}>
                          {t("users.managePermissions")}
                        </DropdownMenuItem>
                      ) : null}
                      {canManage ? (
                        <>
                          <DropdownMenuSeparator />
                          {isLocked ? (
                            <DropdownMenuItem
                              onClick={() =>
                                user.id &&
                                statusAction.mutate({
                                  id: user.id,
                                  action: "unlock",
                                })
                              }
                            >
                              {t("users.unlock")}
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem onClick={() => setLockUser(user)}>
                              {t("users.lock")}
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuItem
                            onClick={() =>
                              user.id &&
                              statusAction.mutate({
                                id: user.id,
                                action: "activate",
                              })
                            }
                          >
                            {t("users.activate")}
                          </DropdownMenuItem>
                          <DropdownMenuItem
                            onClick={() =>
                              user.id &&
                              statusAction.mutate({
                                id: user.id,
                                action: "deactivate",
                              })
                            }
                          >
                            {t("users.deactivate")}
                          </DropdownMenuItem>
                        </>
                      ) : null}
                      {canDelete ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleteUser(user)}
                          >
                            {t("common.delete")}
                          </DropdownMenuItem>
                        </>
                      ) : null}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )
            },
          } satisfies ColumnDef<UserDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("users.title")}
        description={t("users.subtitle")}
        actions={
          canCreate ? (
            <Button
              onClick={() => {
                setEditing(undefined)
                setFormOpen(true)
              }}
            >
              <Plus />
              {t("users.newUser")}
            </Button>
          ) : null
        }
      />

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
        columns={columns}
        data={query.data?.users ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
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

      <UserFormDialog
        open={formOpen}
        onOpenChange={setFormOpen}
        user={editing}
      />
      {rolesUser ? (
        <UserRolesDialog
          open={Boolean(rolesUser)}
          onOpenChange={(open) => !open && setRolesUser(undefined)}
          user={rolesUser}
        />
      ) : null}
      {permsUser ? (
        <UserPermissionsDialog
          open={Boolean(permsUser)}
          onOpenChange={(open) => !open && setPermsUser(undefined)}
          user={permsUser}
        />
      ) : null}

      <ConfirmDialog
        open={Boolean(lockUser)}
        onOpenChange={(open) => {
          if (!open) {
            setLockUser(undefined)
            setLockReason("")
          }
        }}
        title={t("users.lockTitle")}
        confirmLabel={t("users.lock")}
        destructive
        loading={statusAction.isPending}
        onConfirm={() =>
          lockUser?.id &&
          statusAction.mutate({
            id: lockUser.id,
            action: "lock",
            reason: lockReason,
          })
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
        open={Boolean(deleteUser)}
        onOpenChange={(open) => !open && setDeleteUser(undefined)}
        title={t("users.deleteTitle")}
        description={t("users.deleteBody", {
          name:
            deleteUser?.displayName ||
            fullName(
              deleteUser?.firstName,
              deleteUser?.lastName,
              deleteUser?.email ?? ""
            ),
        })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleteUser?.id && deleteMutation.mutate(deleteUser.id)}
      />
    </div>
  )
}
