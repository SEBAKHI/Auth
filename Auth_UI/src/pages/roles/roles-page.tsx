import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { ApplicationSelect } from "@/components/common/application-select"
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
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"
import { RoleFormDialog } from "./role-form-dialog"

type RoleDto = Schemas["RoleDto"]

export function RolesPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [applicationId, setApplicationId] = React.useState<string>()
  const [formOpen, setFormOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<RoleDto | undefined>()
  const [deleting, setDeleting] = React.useState<RoleDto | undefined>()

  const canCreate = hasPermission(PERMISSIONS.roles.create)
  const canUpdate = hasPermission(PERMISSIONS.roles.update)
  const canDelete = hasPermission(PERMISSIONS.roles.delete)

  const query = useQuery({
    queryKey: ["roles", { applicationId }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Roles", {
          params: { query: applicationId ? { applicationId } : {} },
        })
      ),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Roles/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["roles"] })
      toast.success(t("roles.deleted"))
      setDeleting(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<RoleDto, unknown>[] = [
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
      id: "permissions",
      accessorFn: (row) => row.permissions?.length ?? 0,
      header: t("roles.permissions"),
      meta: { label: t("roles.permissions") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.permissions?.length ?? 0}
        </span>
      ),
    },
    {
      id: "isSystem",
      accessorFn: (row) => (row.isSystem ? "true" : "false"),
      filterFn: "faceted",
      header: t("roles.system"),
      meta: {
        label: t("roles.system"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "true", label: t("common.yes") },
          { value: "false", label: t("common.no") },
        ],
      },
      cell: ({ row }) =>
        row.original.isSystem ? (
          <Badge variant="outline">{t("roles.system")}</Badge>
        ) : (
          <span className="text-sm text-muted-foreground">—</span>
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
              const role = row.original
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
                      <DropdownMenuItem
                        onClick={() => navigate(`/roles/${role.id}`)}
                      >
                        {t("common.view")}
                      </DropdownMenuItem>
                      {canUpdate ? (
                        <DropdownMenuItem
                          onClick={() => {
                            setEditing(role)
                            setFormOpen(true)
                          }}
                        >
                          {t("common.edit")}
                        </DropdownMenuItem>
                      ) : null}
                      {canDelete && !role.isSystem ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleting(role)}
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
          } satisfies ColumnDef<RoleDto, unknown>,
        ],
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("roles.title")}
        description={t("roles.subtitle")}
        actions={
          canCreate ? (
            <Button
              onClick={() => {
                setEditing(undefined)
                setFormOpen(true)
              }}
            >
              <Plus />
              {t("roles.newRole")}
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
        tableId="roles"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        enableRowDetail={false}
      />

      <RoleFormDialog
        open={formOpen}
        onOpenChange={setFormOpen}
        role={editing}
        defaultApplicationId={applicationId}
      />

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(undefined)}
        title={t("roles.deleteTitle")}
        description={t("roles.deleteBody", { name: deleting?.name })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleting?.id && deleteMutation.mutate(deleting.id)}
      />
    </div>
  )
}
