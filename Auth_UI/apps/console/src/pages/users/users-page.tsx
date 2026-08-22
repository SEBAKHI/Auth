import { useQuery } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { SearchInput } from "@authsystem/ui/common/search-input"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Switch } from "@authsystem/ui/switch"
import { api } from "@authsystem/api/client"
import {
  collectAllPages,
  toSortParams,
  unwrap,
  toNumber,
} from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"
import { userHref } from "@/lib/record-hrefs"
import { formatDateTime, fullName, userStatusMeta } from "@authsystem/ui/format"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import {
  booleanUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import type { Schemas } from "@authsystem/api/types"
import { useUserActions } from "./use-user-actions"
import { UserFormDialog } from "./user-form-dialog"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

type UserDto = Schemas["UserDto"]

type UserListFilters = { includeDeleted: boolean }

const USERS_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: SORTABLE_COLUMNS.users,
  defaultSorting: [{ id: "createdAt", desc: true }],
  filters: { includeDeleted: booleanUrlFilter() },
} satisfies ListUrlStateOptions<UserListFilters>

export function UsersPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const {
    pageIndex: page,
    pageSize,
    search: searchInput,
    sorting,
    filters: { includeDeleted: showDeleted },
    setSearch: setSearchInput,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilter,
  } = useListUrlState(USERS_LIST_URL_OPTIONS)
  const search = useDebouncedValue(searchInput)
  const { sortBy, sortDirection } = toSortParams(sorting)

  const [formOpen, setFormOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<UserDto | undefined>()
  const [rolesUser, setRolesUser] = React.useState<UserDto | undefined>()
  const [permsUser, setPermsUser] = React.useState<UserDto | undefined>()
  const [lockUser, setLockUser] = React.useState<UserDto | undefined>()
  const [deactivateUser, setDeactivateUser] = React.useState<
    UserDto | undefined
  >()
  const [lockReason, setLockReason] = React.useState("")
  const [deleteUser, setDeleteUser] = React.useState<UserDto | undefined>()
  const [hardDeleteUser, setHardDeleteUser] = React.useState<
    UserDto | undefined
  >()
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
      setDeactivateUser(undefined)
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
        row.displayName ||
        fullName(row.firstName, row.lastName, row.email ?? ""),
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
          <RecordLink
            href={userHref(user.id)}
            className="min-w-0 text-start hover:underline"
          >
            <p className="truncate font-medium">{name}</p>
          </RecordLink>
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
          return <Badge variant="destructive">{t("users.deletedStatus")}</Badge>
        }
        const meta = userStatusMeta(row.original.status)
        return <Badge variant={meta.variant}>{t(`common.${meta.key}`)}</Badge>
      },
    },
    {
      id: "roles",
      // The API cannot order by a user's role count; offering the header would
      // send a sortBy the endpoint rejects.
      enableSorting: false,
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
        header: () => <span className="sr-only">{t("common.actions")}</span>,
        cell: ({ row }) => {
          const user = row.original
          const viewHref = userHref(user.id)
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
                    <DropdownMenuGroup>
                      <DropdownMenuItem
                        variant="destructive"
                        onClick={() => {
                          setHardDeleteConfirm("")
                          setHardDeleteUser(user)
                        }}
                      >
                        {t("users.hardDelete")}
                      </DropdownMenuItem>
                    </DropdownMenuGroup>
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
                  <DropdownMenuGroup>
                    {viewHref ? (
                      <DropdownMenuItem asChild>
                        <Link to={viewHref}>{t("common.view")}</Link>
                      </DropdownMenuItem>
                    ) : null}
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
                  </DropdownMenuGroup>
                  {canManage ? (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
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
                        {/* Activating and unlocking restore access, so they act
                            on the click. Deactivating removes it - the account
                            is signed out everywhere - and a row menu is one
                            mis-aimed pointer away from the wrong person. */}
                        <DropdownMenuItem
                          onClick={() => setDeactivateUser(user)}
                        >
                          {t("users.deactivate")}
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                    </>
                  ) : null}
                  {canDelete ? (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
                        <DropdownMenuItem
                          variant="destructive"
                          onClick={() => setDeleteUser(user)}
                        >
                          {t("common.delete")}
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
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
    <div className="flex min-h-0 flex-1 flex-col gap-6">
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
          onChange={setSearchInput}
          placeholder={t("users.searchPlaceholder")}
          className="w-full max-w-sm"
        />
        {canManage ? (
          <Field orientation="horizontal" className="w-auto">
            <Switch
              id="show-deleted-users"
              checked={showDeleted}
              onCheckedChange={(checked) =>
                setFilter("includeDeleted", checked)
              }
            />
            <FieldLabel htmlFor="show-deleted-users">
              {t("users.showDeleted")}
            </FieldLabel>
          </Field>
        ) : null}
      </div>

      <DataTable
        fillHeight
        tableId="users"
        columns={columns}
        data={query.data?.users ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onExportAll={exportAll}
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
        open={Boolean(deactivateUser)}
        onOpenChange={(open) => !open && setDeactivateUser(undefined)}
        title={t("users.deactivateTitle")}
        description={t("users.deactivateBody", {
          name:
            deactivateUser?.displayName ||
            fullName(
              deactivateUser?.firstName,
              deactivateUser?.lastName,
              deactivateUser?.email ?? ""
            ),
        })}
        confirmLabel={t("users.deactivate")}
        destructive
        loading={statusAction.isPending}
        onConfirm={() =>
          deactivateUser?.id &&
          statusAction.mutate({
            id: deactivateUser.id,
            action: "deactivate",
          })
        }
      />

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
            placeholder={t("users.lockReasonPlaceholder")}
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
