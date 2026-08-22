import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { organizationHref } from "../../lib/record-hrefs"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import {
  enumArrayUrlFilter,
  stringArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
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
import { api } from "@authsystem/api/client"
import { DEFAULT_PAGE_SIZE } from "@authsystem/api/constants"
import { unwrap } from "@authsystem/api/helpers"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { OrganizationFormDialog } from "./organization-form-dialog"

type OrganizationSummaryDto = Schemas["OrganizationSummaryDto"]

type OrganizationListFilters = {
  roles: string[]
  statuses: Array<"active" | "inactive">
}

const ORGANIZATIONS_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: ["name", "memberCount", "userRole", "status"],
  defaultSorting: [],
  filters: {
    roles: stringArrayUrlFilter({ param: "role", maxItems: 10 }),
    statuses: enumArrayUrlFilter(["active", "inactive"], "status"),
  },
} satisfies ListUrlStateOptions<OrganizationListFilters>

export function OrganizationsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const {
    search,
    sorting,
    filters: { roles, statuses },
    setSearch,
    setSorting,
    setFilters,
  } = useListUrlState(ORGANIZATIONS_LIST_URL_OPTIONS)
  const columnFilters: ColumnFiltersState = [
    ...(roles.length ? [{ id: "userRole", value: roles }] : []),
    ...(statuses.length ? [{ id: "status", value: statuses }] : []),
  ]
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      roles:
        (next.find((filter) => filter.id === "userRole")?.value as
          | string[]
          | undefined) ?? [],
      statuses:
        (next.find((filter) => filter.id === "status")?.value as
          | OrganizationListFilters["statuses"]
          | undefined) ?? [],
    })

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
        <RecordLink
          href={organizationHref(row.original.id)}
          className="min-w-0 text-start hover:underline"
        >
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.code}
          </p>
        </RecordLink>
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
        const viewHref = organizationHref(org.id)
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
                  {viewHref ? (
                    <DropdownMenuItem asChild>
                      <Link to={viewHref}>{t("common.view")}</Link>
                    </DropdownMenuItem>
                  ) : null}
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuGroup>
                  <DropdownMenuItem
                    variant="destructive"
                    onClick={() => setDeleting(org)}
                  >
                    {t("common.delete")}
                  </DropdownMenuItem>
                </DropdownMenuGroup>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        )
      },
    },
  ]

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
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
        fillHeight
        tableId="organizations"
        globalSearch
        globalFilter={search}
        onGlobalFilterChange={setSearch}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        sorting={sorting}
        onSortingChange={setSorting}
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
