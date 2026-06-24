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
import { api } from "@/lib/api/client"
import { unwrap, toNumber } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { DEFAULT_PAGE_SIZE, PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import { formatDate } from "@/lib/format"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import type { Schemas } from "@/lib/api/types"
import {
  ApplicationCreateDialog,
  ApplicationEditDialog,
} from "./application-dialogs"

type ApplicationDto = Schemas["ApplicationDto"]

export function ApplicationsPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)

  const [createOpen, setCreateOpen] = React.useState(false)
  const [editing, setEditing] = React.useState<ApplicationDto | undefined>()
  const [deleting, setDeleting] = React.useState<ApplicationDto | undefined>()

  const canCreate = hasPermission(PERMISSIONS.applications.create)
  const canUpdate = hasPermission(PERMISSIONS.applications.update)
  const canDelete = hasPermission(PERMISSIONS.applications.delete)
  const hasRowActions = canUpdate || canDelete

  const query = useQuery({
    queryKey: ["applications", { page, pageSize, search }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              search: search || undefined,
            },
          },
        })
      ),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Applications/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      toast.success(t("applications.deleted"))
      setDeleting(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<ApplicationDto, unknown>[] = [
    {
      header: t("common.name"),
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </div>
      ),
    },
    {
      header: t("common.status"),
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "default" : "secondary"}>
          {row.original.isActive ? t("common.active") : t("common.inactive")}
        </Badge>
      ),
    },
    {
      header: t("applications.contactEmail"),
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.contactEmail ?? "—"}
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
              const app = row.original
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
                        <DropdownMenuItem onClick={() => setEditing(app)}>
                          {t("common.edit")}
                        </DropdownMenuItem>
                      ) : null}
                      {canDelete ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setDeleting(app)}
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
          } satisfies ColumnDef<ApplicationDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("applications.title")}
        description={t("applications.subtitle")}
        actions={
          canCreate ? (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus />
              {t("applications.newApplication")}
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
          placeholder={t("common.search")}
          className="ps-8"
        />
      </div>

      <DataTable
        columns={columns}
        data={query.data?.applications ?? []}
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

      <ApplicationCreateDialog open={createOpen} onOpenChange={setCreateOpen} />
      {editing ? (
        <ApplicationEditDialog
          open={Boolean(editing)}
          onOpenChange={(open) => !open && setEditing(undefined)}
          application={editing}
        />
      ) : null}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(undefined)}
        title={t("applications.deleteTitle")}
        description={t("applications.deleteBody", { name: deleting?.name })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleting?.id && deleteMutation.mutate(deleting.id)}
      />
    </div>
  )
}
