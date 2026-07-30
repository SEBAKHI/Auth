import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
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
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"
import { OrganizationFormDialog } from "./organization-form-dialog"

type OrganizationSummaryDto = Schemas["OrganizationSummaryDto"]

export function OrganizationsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [createOpen, setCreateOpen] = React.useState(false)
  const [deleting, setDeleting] = React.useState<
    OrganizationSummaryDto | undefined
  >()

  const query = useQuery({
    queryKey: ["organizations"],
    queryFn: () => unwrap(api.GET("/api/v1/Organizations")),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Organizations/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["organizations"] })
      toast.success(t("organizations.deleted"))
      setDeleting(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<OrganizationSummaryDto, unknown>[] = [
    avatarColumn<OrganizationSummaryDto>({
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
      id: "memberCount",
      accessorFn: (row) => row.memberCount ?? 0,
      header: t("organizations.memberCount"),
      meta: { label: t("organizations.memberCount") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.memberCount ?? 0}
        </span>
      ),
    },
    {
      id: "userRole",
      accessorFn: (row) => row.userRole ?? "",
      filterFn: "faceted",
      header: t("common.role"),
      meta: { label: t("common.role"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.userRole ?? "—"}
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
      id: "actions",
      enableSorting: false,
      enableHiding: false,
      header: () => <span className="sr-only">{t("common.actions")}</span>,
      cell: ({ row }) => {
        const org = row.original
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
                  onClick={() => navigate(`/organizations/${org.id}`)}
                >
                  {t("common.view")}
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  variant="destructive"
                  onClick={() => setDeleting(org)}
                >
                  {t("common.delete")}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        )
      },
    },
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("organizations.title")}
        description={t("organizations.subtitle")}
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <Plus data-icon="inline-start" />
            {t("organizations.newOrganization")}
          </Button>
        }
      />

      <DataTable
        tableId="organizations"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        // Row click navigates to the full organization detail page, so the
        // generic detail panel is disabled here (CSV export stays on).
        enableRowDetail={false}
      />

      <OrganizationFormDialog open={createOpen} onOpenChange={setCreateOpen} />

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(undefined)}
        title={t("organizations.deleteTitle")}
        description={t("organizations.deleteBody", { name: deleting?.name })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleting?.id && deleteMutation.mutate(deleting.id)}
      />
    </div>
  )
}
