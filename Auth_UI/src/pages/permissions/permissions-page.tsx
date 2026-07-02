import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ApplicationSelect } from "@/components/common/application-select"
import { ConfirmDialog } from "@/components/common/confirm-dialog"
import { PageHeader } from "@/components/common/page-header"
import { DataTable } from "@/components/data-table/data-table"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"
import { PermissionFormDialog } from "./permission-form-dialog"
import { PermissionImplicationsDialog } from "./permission-implications-dialog"

type PermissionDto = Schemas["PermissionDto"]

export function PermissionsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

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
  const hasRowActions = canUpdate || canDelete || canManage

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
        <span className="font-mono text-sm">{row.original.code}</span>
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
    ...(hasRowActions
      ? [
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
                        <DropdownMenuItem onClick={() => setImplications(perm)}>
                          {t("permissions.implications")}
                        </DropdownMenuItem>
                      ) : null}
                      {canDelete ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleting(perm)}
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
          } satisfies ColumnDef<PermissionDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="space-y-6">
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
              <Plus />
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
        tableId="permissions"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onEditRow={
          canUpdate
            ? (perm) => {
                setEditing(perm)
                setFormOpen(true)
              }
            : undefined
        }
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
