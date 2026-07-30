import { useQuery } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { SearchInput } from "@astoom/ui/common/search-input"
import { PageHeader } from "@astoom/ui/common/page-header"
import { avatarColumn } from "@astoom/ui/data-table/columns"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { Field, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Switch } from "@astoom/ui/switch"
import { api } from "@astoom/api/client"
import { collectAllPages, toSortParams, unwrap, toNumber } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { formatDateTime, fullName, userStatusMeta } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import type { Schemas } from "@astoom/api/types"
import { useUserActions } from "./use-user-actions"
import { UserFormDialog } from "./user-form-dialog"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

type UserDto = Schemas["UserDto"]

export function UsersPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "createdAt", desc: true },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const [formOpen, setFormOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<UserDto | undefined>()
  const [rolesUser, setRolesUser] = React.useState<UserDto | undefined>()
  const [permsUser, setPermsUser] = React.useState<UserDto | undefined>()
  const [lockUser, setLockUser] = React.useState<UserDto | undefined>()
  const [lockReason, setLockReason] = React.useState("")
  const [deleteUser, setDeleteUser] = React.useState<UserDto | undefined>()
  const [showDeleted, setShowDeleted] = React.useState(false)
  const [hardDeleteUser, setHardDeleteUser] = React.useState<UserDto | undefined>()
  const [hardDeleteConfirm, setHardDeleteConfirm] = React.useState("")

  const canCreate = hasPermission(PERMISSIONS.users.create)
  const canUpdate = hasPermission(PERMISSIONS.users.update)
  const canDelete = hasPermission(PERMISSIONS.users.delete)
  const canManageRoles = hasPermission(PERMISSIONS.users.manageRoles)
  const canManagePerms = hasPermission(PERMISSIONS.users.managePermissions)
  const canManage = hasPermission(PERMISSIONS.users.manage)

  // Only users:manage callers may request deleted accounts; the API rejects
  // the flag for anyone else, so it is simply never sent in that case.
  const includeDeleted = (canManage && showDeleted) || undefined

  const query = useQuery({
    queryKey: [
      "users",
      { page, pageSize, search, sortBy, sortDirection, includeDeleted },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              searchTerm: search || undefined,
              sortBy,
              sortDirection,
              includeDeleted,
            },
          },
        })
      ),
  })

  const exportAll = React.useCallback(
    () =>
      collectAllPages<UserDto>(async (pageNumber, size) => {
        const result = await unwrap(
          api.GET("/api/v1/Users", {
            params: {
              query: {
                pageNumber,
                pageSize: size,
                searchTerm: search || undefined,
                sortBy,
                sortDirection,
                includeDeleted,
              },
            },
          })
        )
        return {
          items: result.users ?? [],
          totalCount: toNumber(result.totalCount),
        }
      }),
    [search, sortBy, sortDirection, includeDeleted]
  )

  const { statusAction, deleteMutation, hardDeleteMutation } = useUserActions({
    onStatusChanged: () => {
      setLockUser(undefined)
      setLockReason("")
    },
    onDeleted: () => setDeleteUser(undefined),
    onHardDeleted: () => {
      setHardDeleteUser(undefined)
      setHardDeleteConfirm("")
    },
  })

  const columns: ColumnDef<UserDto, unknown>[] = [
    avatarColumn<UserDto>({
      getSrc: (row) => row.profileImageUrl,
      getName: (row) =>
        row.displayName ||
        fullName(row.firstName, row.lastName, row.email ?? ""),
    }),
    {
      id: "name",
      accessorFn: (row) =>
        row.displayName || fullName(row.firstName, row.lastName, row.email ?? ""),
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => {
        const user = row.original
        const name =
          user.displayName ||
          fullName(user.firstName, user.lastName, user.email ?? "")
        // Deleted accounts have no detail page (operational reads exclude
        // them), so their name is plain text instead of a link.
        if (user.isDeleted) {
          return (
            <p className="truncate font-medium text-muted-foreground">{name}</p>
          )
        }
        return (
          <button
            type="button"
            className="min-w-0 text-start hover:underline"
            onClick={() => navigate(`/users/${user.id}`)}
          >
            <p className="truncate font-medium">{name}</p>
          </button>
        )
      },
    },
    {
      accessorKey: "email",
      header: t("common.email"),
      meta: { label: t("common.email") },
    },
    {
      id: "status",
      accessorFn: (row) =>
        row.isDeleted ? "deleted" : userStatusMeta(row.status).key,
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
          ...(showDeleted
            ? [{ value: "deleted", label: t("users.deletedStatus") }]
            : []),
        ],
      },
      cell: ({ row }) => {
        if (row.original.isDeleted) {
          return (
            <Badge variant="destructive">{t("users.deletedStatus")}</Badge>
          )
        }
        const meta = userStatusMeta(row.original.status)
        return <Badge variant={meta.variant}>{t(`common.${meta.key}`)}</Badge>
      },
    },
    {
      id: "roles",
      accessorFn: (row) => row.roles?.length ?? 0,
      header: t("users.roles"),
      meta: { label: t("users.roles") },
      cell: ({ row }) => {
        const roles = row.original.roles ?? []
        return (
          <span className="text-sm text-muted-foreground">
            {roles.length > 0 ? roles.length : "—"}
          </span>
        )
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
    ...[
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => (
              <span className="sr-only">{t("common.actions")}</span>
            ),
            cell: ({ row }) => {
              const user = row.original
              const isLocked = userStatusMeta(user.status).key === "locked"
              // A deleted account supports exactly one action: permanent
              // removal, offered only to user managers.
              if (user.isDeleted) {
                if (!canManage) return null
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
                        <DropdownMenuItem
                          variant="destructive"
                          onClick={() => {
                            setHardDeleteConfirm("")
                            setHardDeleteUser(user)
                          }}
                        >
                          {t("users.hardDelete")}
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                )
              }
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
                      <DropdownMenuItem
                        onClick={() => navigate(`/users/${user.id}`)}
                      >
                        {t("common.view")}
                      </DropdownMenuItem>
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
        ],
  ]

  return (
    <div className="flex flex-col gap-6">
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
              <Plus data-icon="inline-start" />
              {t("users.newUser")}
            </Button>
          ) : null
        }
      />

      <div className="flex flex-wrap items-center justify-between gap-4">
        <SearchInput
        value={searchInput}
        onChange={(value) => {
              setSearchInput(value)
              setPage(0)
            }}
        placeholder={t("users.searchPlaceholder")}
        className="w-full max-w-sm"
      />
        {canManage ? (
          <Field orientation="horizontal" className="w-auto">
            <Switch
              id="show-deleted-users"
              checked={showDeleted}
              onCheckedChange={(checked) => {
                setShowDeleted(checked)
                setPage(0)
              }}
            />
            <FieldLabel htmlFor="show-deleted-users">
              {t("users.showDeleted")}
            </FieldLabel>
          </Field>
        ) : null}
      </div>

      <DataTable
        tableId="users"
        columns={columns}
        data={query.data?.users ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onExportAll={exportAll}
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
        <Field>
          <FieldLabel htmlFor="lock-reason">{t("users.lockReason")}</FieldLabel>
          <Input
            id="lock-reason"
            value={lockReason}
            onChange={(e) => setLockReason(e.target.value)}
          />
        </Field>
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

      <ConfirmDialog
        open={Boolean(hardDeleteUser)}
        onOpenChange={(open) => {
          if (!open) {
            setHardDeleteUser(undefined)
            setHardDeleteConfirm("")
          }
        }}
        title={t("users.hardDeleteTitle")}
        description={t("users.hardDeleteBody", {
          name:
            hardDeleteUser?.displayName ||
            fullName(
              hardDeleteUser?.firstName,
              hardDeleteUser?.lastName,
              hardDeleteUser?.email ?? ""
            ),
        })}
        confirmLabel={t("users.hardDelete")}
        destructive
        loading={hardDeleteMutation.isPending}
        confirmDisabled={
          hardDeleteConfirm.trim().toLowerCase() !==
          (hardDeleteUser?.email ?? "").toLowerCase()
        }
        onConfirm={() =>
          hardDeleteUser?.id && hardDeleteMutation.mutate(hardDeleteUser.id)
        }
      >
        <Field>
          <FieldLabel htmlFor="hard-delete-confirm">
            {t("users.hardDeleteConfirmHint", {
              email: hardDeleteUser?.email ?? "",
            })}
          </FieldLabel>
          <Input
            id="hard-delete-confirm"
            value={hardDeleteConfirm}
            onChange={(e) => setHardDeleteConfirm(e.target.value)}
            autoComplete="off"
            spellCheck={false}
          />
        </Field>
      </ConfirmDialog>
    </div>
  )
}
