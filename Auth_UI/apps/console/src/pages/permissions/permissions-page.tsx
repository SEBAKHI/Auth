import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { PermissionCode } from "@authsystem/ui/common/permission-code"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { useSearchHandoff } from "@authsystem/ui/hooks/use-search-query"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { PermissionFormDialog } from "./permission-form-dialog"
import { PermissionImplicationsDialog } from "./permission-implications-dialog"

type PermissionDto = Schemas["PermissionDto"]

export function PermissionsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // A term the command palette handed over, so arriving from "see all N in
  // Permissions" lands on those rows rather than on the whole list again.
  const handoff = useSearchHandoff()

  const [applicationId, setApplicationId] = React.useState<string>()
  const [formOpen, setFormOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<PermissionDto | undefined>()
  const [implications, setImplications] = React.useState<
    PermissionDto | undefined
  >()
  const [deleting, setDeleting] = React.useState<PermissionDto | undefined>()

  const canCreate = hasPermission(PERMISSIONS.permissions.create)
  const canUpdate = hasPermission(PERMISSIONS.permissions.update)
  const canDelete = hasPermission(PERMISSIONS.permissions.delete)
  const canManage = hasPermission(PERMISSIONS.permissions.manage)

  const query = useQuery({
    queryKey: ["permissions", { applicationId }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Permissions", {
          params: { query: applicationId ? { applicationId } : {} },
        })
      ),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Permissions/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["permissions"] })
      toast.success(t("permissions.deleted"))
      setDeleting(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<PermissionDto, unknown>[] = [
    {
      id: "code",
      accessorFn: (row) => row.code ?? "",
      header: t("common.code"),
      meta: { label: t("common.code") },
      cell: ({ row }) => (
        <button
          type="button"
          className="text-start hover:underline"
          onClick={() => navigate(`/permissions/${row.original.id}`)}
        >
          <PermissionCode
            code={row.original.code ?? ""}
            className="font-mono text-sm"
          />
        </button>
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
    ...[
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => (
              <span className="sr-only">{t("common.actions")}</span>
            ),
            cell: ({ row }) => {
              const perm = row.original
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
                    <DropdownMenuContent align="end">
                      <DropdownMenuGroup>
                        <DropdownMenuItem
                          onClick={() => navigate(`/permissions/${perm.id}`)}
                        >
                          {t("common.view")}
                        </DropdownMenuItem>
                        {canUpdate ? (
                          <DropdownMenuItem
                            onClick={() => {
                              setEditing(perm)
                              setFormOpen(true)
                            }}
                          >
                            {t("common.edit")}
                          </DropdownMenuItem>
                        ) : null}
                        {canManage ? (
                          <DropdownMenuItem
                            onClick={() => setImplications(perm)}
                          >
                            {t("permissions.implications")}
                          </DropdownMenuItem>
                        ) : null}
                      </DropdownMenuGroup>
                      {canDelete ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuGroup>
                            <DropdownMenuItem
                              variant="destructive"
                              onClick={() => setDeleting(perm)}
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
          } satisfies ColumnDef<PermissionDto, unknown>,
        ],
  ]

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <PageHeader
        title={t("permissions.title")}
        description={t("permissions.subtitle")}
        actions={
          canCreate ? (
            <Button
              onClick={() => {
                setEditing(undefined)
                setFormOpen(true)
              }}
            >
              <Plus data-icon="inline-start" />
              {t("permissions.newPermission")}
            </Button>
          ) : null
        }
      />

      <div className="max-w-xs">
        <ApplicationSelect
          value={applicationId}
          onChange={setApplicationId}
          allowAll
          className="w-full"
        />
      </div>

      <DataTable
        fillHeight
        tableId="permissions"
        globalSearch
        initialGlobalFilter={handoff}
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        enableRowDetail={false}
      />

      <PermissionFormDialog
        open={formOpen}
        onOpenChange={setFormOpen}
        permission={editing}
        defaultApplicationId={applicationId}
      />
      {implications ? (
        <PermissionImplicationsDialog
          open={Boolean(implications)}
          onOpenChange={(open) => !open && setImplications(undefined)}
          permission={implications}
        />
      ) : null}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(undefined)}
        title={t("permissions.deleteTitle")}
        description={t("permissions.deleteBody", { name: deleting?.name })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleting?.id && deleteMutation.mutate(deleting.id)}
      />
    </div>
  )
}
