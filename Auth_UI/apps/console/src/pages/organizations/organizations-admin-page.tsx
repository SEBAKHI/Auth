import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

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
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { api } from "@astoom/api/client"
import {
  collectAllPages,
  toSortParams,
  unwrap,
  toNumber,
} from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import { useAuth } from "@astoom/auth/auth-context"
import { PERMISSIONS, DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { formatDateTime } from "@astoom/ui/format"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import type { Schemas } from "@astoom/api/types"
import { OrganizationFormDialog } from "@astoom/account/pages/organizations/organization-form-dialog"

type OrganizationDto = Schemas["OrganizationDto"]

/** Platform administration over ALL organizations (server-side paged). */
export function OrganizationsAdminPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebouncedValue(searchInput)
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "name", desc: false },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const [deleting, setDeleting] = React.useState<OrganizationDto | undefined>()
  const [createOpen, setCreateOpen] = React.useState(false)
  const canManage = hasPermission(PERMISSIONS.organizations.manage)

  const query = useQuery({
    queryKey: [
      "organizations-all",
      { page, pageSize, search, sortBy, sortDirection },
    ],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/all", {
          params: {
            query: {
              pageNumber: page + 1,
              pageSize,
              searchTerm: search || undefined,
              sortBy,
              sortDirection,
            },
          },
        })
      ),
  })

  const exportAll = React.useCallback(
    () =>
      collectAllPages<OrganizationDto>(async (pageNumber, size) => {
        const result = await unwrap(
          api.GET("/api/v1/Organizations/all", {
            params: {
              query: {
                pageNumber,
                pageSize: size,
                searchTerm: search || undefined,
                sortBy,
                sortDirection,
              },
            },
          })
        )
        return {
          items: result.organizations ?? [],
          totalCount: toNumber(result.totalCount),
        }
      }),
    [search, sortBy, sortDirection]
  )

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Organizations/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["organizations-all"] })
      toast.success(t("organizations.deleted"))
      setDeleting(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<OrganizationDto, unknown>[] = [
    avatarColumn<OrganizationDto>({
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
      id: "owner",
      enableSorting: false,
      accessorFn: (row) => row.ownerName ?? row.ownerEmail ?? "",
      header: t("organizations.owner"),
      meta: { label: t("organizations.owner") },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate text-sm">{row.original.ownerName || "—"}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.ownerEmail}
          </p>
        </div>
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
      id: "enabledAppCount",
      enableSorting: false,
      accessorFn: (row) => row.enabledAppCount ?? 0,
      header: t("organizations.enabledAppCount"),
      meta: { label: t("organizations.enabledAppCount") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.enabledAppCount ?? 0}
        </span>
      ),
    },
    {
      id: "isActive",
      accessorFn: (row) => (row.isActive ? "active" : "inactive"),
      header: t("common.status"),
      meta: { label: t("common.status") },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "default" : "secondary"}>
          {row.original.isActive ? t("common.active") : t("common.inactive")}
        </Badge>
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
                <DropdownMenuGroup>
                  <DropdownMenuItem
                    onClick={() => navigate(`/organizations/${org.id}`)}
                  >
                    {t("common.view")}
                  </DropdownMenuItem>
                </DropdownMenuGroup>
                {canManage ? (
                  <>
                    <DropdownMenuSeparator />
                    <DropdownMenuGroup>
                      <DropdownMenuItem
                        variant="destructive"
                        onClick={() => setDeleting(org)}
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
    },
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("organizations.title")}
        description={t("organizations.subtitle")}
        actions={
          canManage ? (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus data-icon="inline-start" />
              {t("organizations.newOrganization")}
            </Button>
          ) : undefined
        }
      />

      <SearchInput
        value={searchInput}
        onChange={(value) => {
            setSearchInput(value)
            setPage(0)
          }}
        placeholder={t("organizations.searchPlaceholder")}
      />

      <DataTable
        tableId="organizations-all"
        columns={columns}
        data={query.data?.organizations ?? []}
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
