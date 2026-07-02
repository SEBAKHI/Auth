import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, SortingState } from "@tanstack/react-table"
import { ArrowLeft, Loader2, MoreHorizontal, Plus, Send } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { toast } from "sonner"

import { ApplicationSelect } from "@/components/common/application-select"
import { ConfirmDialog } from "@/components/common/confirm-dialog"
import { PageHeader } from "@/components/common/page-header"
import { DataTable } from "@/components/data-table/data-table"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { useDirtyClose } from "@/hooks/use-dirty-close"
import { api } from "@/lib/api/client"
import { collectAllPages, toSortParams, unwrap, toNumber } from "@/lib/api/helpers"
import { DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { getErrorMessage } from "@/lib/errors"
import { formatDate, fullName } from "@/lib/format"
import type { Schemas } from "@/lib/api/types"
import { OrganizationFormDialog } from "./organization-form-dialog"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function MembersTab({ orgId }: { orgId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [page, setPage] = React.useState(0)
  const [pageSize, setPageSize] = React.useState(DEFAULT_PAGE_SIZE)
  const [removing, setRemoving] =
    React.useState<Schemas["OrganizationMemberDto"]>()
  // Server-side sort over the whole dataset; initial value mirrors the API default.
  const [sorting, setSorting] = React.useState<SortingState>([
    { id: "joinedAt", desc: false },
  ])
  const { sortBy, sortDirection } = toSortParams(sorting)

  const query = useQuery({
    queryKey: ["org-members", orgId, { page, pageSize, sortBy, sortDirection }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/members", {
          params: {
            path: { id: orgId },
            query: { pageNumber: page + 1, pageSize, sortBy, sortDirection },
          },
        })
      ),
  })

  const exportAll = React.useCallback(
    () =>
      collectAllPages<Schemas["OrganizationMemberDto"]>(
        async (pageNumber, size) => {
          const result = await unwrap(
            api.GET("/api/v1/Organizations/{id}/members", {
              params: {
                path: { id: orgId },
                query: { pageNumber, pageSize: size, sortBy, sortDirection },
              },
            })
          )
          return {
            items: result.members ?? [],
            totalCount: toNumber(result.totalCount),
          }
        }
      ),
    [orgId, sortBy, sortDirection]
  )

  const removeMutation = useMutation({
    mutationFn: async (userId: string) => {
      const { error } = await api.DELETE(
        "/api/v1/Organizations/{orgId}/members/{userId}",
        { params: { path: { orgId, userId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["org-members", orgId] })
      toast.success(t("organizations.memberRemoved"))
      setRemoving(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<Schemas["OrganizationMemberDto"], unknown>[] = [
    {
      id: "name",
      accessorFn: (row) =>
        row.fullName || fullName(row.firstName, row.lastName, row.email ?? ""),
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">
            {row.original.fullName ||
              fullName(
                row.original.firstName,
                row.original.lastName,
                row.original.email ?? ""
              )}
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {row.original.email}
          </p>
        </div>
      ),
    },
    {
      accessorKey: "roleName",
      filterFn: "faceted",
      header: t("common.role"),
      meta: { label: t("common.role"), filterVariant: "faceted" },
    },
    {
      id: "joinedAt",
      accessorFn: (row) => row.joinedAt ?? "",
      header: t("common.createdAt"),
      meta: { label: t("common.createdAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.original.joinedAt)}
        </span>
      ),
    },
    {
      id: "actions",
      enableSorting: false,
      enableHiding: false,
      header: () => <span className="sr-only">{t("common.actions")}</span>,
      cell: ({ row }) => (
        <div className="text-end">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setRemoving(row.original)}
          >
            {t("organizations.removeMember")}
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <DataTable
        tableId="org-members"
        columns={columns}
        data={query.data?.members ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        onExportAll={exportAll}
        sorting={sorting}
        onSortingChange={(next) => {
          setSorting(next)
          setPage(0)
        }}
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
      <ConfirmDialog
        open={Boolean(removing)}
        onOpenChange={(open) => !open && setRemoving(undefined)}
        title={t("organizations.removeMember")}
        description={removing?.email}
        confirmLabel={t("common.remove")}
        destructive
        loading={removeMutation.isPending}
        onConfirm={() =>
          removing?.userId && removeMutation.mutate(removing.userId)
        }
      />
    </>
  )
}

function InviteDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [email, setEmail] = React.useState("")
  const [roleId, setRoleId] = React.useState<string>()

  React.useEffect(() => {
    if (open) {
      setEmail("")
      setRoleId(undefined)
    }
  }, [open])

  const rolesQuery = useQuery({
    queryKey: ["roles", "all"],
    enabled: open,
    queryFn: () => unwrap(api.GET("/api/v1/Roles", { params: { query: {} } })),
  })

  const mutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{id}/invitations",
        {
          params: { path: { id: orgId } },
          body: { email, roleId: roleId ?? "" },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["org-invitations", orgId],
      })
      toast.success(t("organizations.invited"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const valid = EMAIL_RE.test(email) && Boolean(roleId)

  const { requestOpenChange, discardDialog } = useDirtyClose({
    isDirty: Boolean(email) || Boolean(roleId),
    onOpenChange,
  })

  return (
    <Dialog open={open} onOpenChange={requestOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("organizations.inviteMember")}</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div className="space-y-2">
            <Label>{t("organizations.inviteEmail")}</Label>
            <Input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div className="space-y-2">
            <Label>{t("common.role")}</Label>
            <Select value={roleId} onValueChange={setRoleId}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder={t("common.role")} />
              </SelectTrigger>
              <SelectContent>
                {(rolesQuery.data ?? [])
                  .filter((role) => role.id)
                  .map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => requestOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!valid || mutation.isPending}
          >
            {mutation.isPending ? <Loader2 className="animate-spin" /> : null}
            {t("organizations.inviteMember")}
          </Button>
        </DialogFooter>
        {discardDialog}
      </DialogContent>
    </Dialog>
  )
}

function InvitationsTab({ orgId }: { orgId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [inviteOpen, setInviteOpen] = React.useState(false)

  const query = useQuery({
    queryKey: ["org-invitations", orgId],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/invitations", {
          params: { path: { id: orgId } },
        })
      ),
  })

  const resendMutation = useMutation({
    mutationFn: async (invitationId: string) => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{orgId}/invitations/{invitationId}/resend",
        { params: { path: { orgId, invitationId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["org-invitations", orgId],
      })
      toast.success(t("organizations.invitationResent"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<Schemas["OrganizationInvitationDto"], unknown>[] = [
    {
      accessorKey: "email",
      header: t("common.email"),
      meta: { label: t("common.email") },
    },
    {
      accessorKey: "roleName",
      filterFn: "faceted",
      header: t("common.role"),
      meta: { label: t("common.role"), filterVariant: "faceted" },
    },
    {
      id: "status",
      accessorFn: (row) => row.status ?? "",
      filterFn: "faceted",
      header: t("common.status"),
      meta: { label: t("common.status"), filterVariant: "faceted" },
      cell: ({ row }) => (
        <Badge variant={row.original.isExpired ? "destructive" : "secondary"}>
          {row.original.isExpired ? t("common.expired") : row.original.status}
        </Badge>
      ),
    },
    {
      id: "expiresAt",
      accessorFn: (row) => row.expiresAt ?? "",
      header: t("common.expiresAt"),
      meta: { label: t("common.expiresAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.original.expiresAt)}
        </span>
      ),
    },
    {
      id: "actions",
      enableSorting: false,
      enableHiding: false,
      header: () => <span className="sr-only">{t("common.actions")}</span>,
      cell: ({ row }) => (
        <div className="text-end">
          <Button
            variant="ghost"
            size="sm"
            disabled={resendMutation.isPending}
            onClick={() =>
              row.original.id && resendMutation.mutate(row.original.id)
            }
          >
            <Send />
            {t("organizations.resendInvite")}
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => setInviteOpen(true)}>
          <Plus />
          {t("organizations.inviteMember")}
        </Button>
      </div>
      <DataTable
        tableId="org-invitations"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
      />
      <InviteDialog
        orgId={orgId}
        open={inviteOpen}
        onOpenChange={setInviteOpen}
      />
    </div>
  )
}

function ApplicationsTab({ orgId }: { orgId: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [enableOpen, setEnableOpen] = React.useState(false)
  const [appId, setAppId] = React.useState<string>()
  const [removing, setRemoving] =
    React.useState<Schemas["OrganizationApplicationDto"]>()

  const query = useQuery({
    queryKey: ["org-apps", orgId],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/applications", {
          params: { path: { id: orgId } },
        })
      ),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["org-apps", orgId] })

  const enableMutation = useMutation({
    mutationFn: async (applicationId: string) => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{id}/applications",
        { params: { path: { id: orgId } }, body: { applicationId } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("organizations.appEnabled"))
      setEnableOpen(false)
      setAppId(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: async (applicationId: string) => {
      const { error } = await api.DELETE(
        "/api/v1/Organizations/{id}/applications/{applicationId}",
        { params: { path: { id: orgId, applicationId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("organizations.appDisabled"))
      setRemoving(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<Schemas["OrganizationApplicationDto"], unknown>[] = [
    {
      accessorKey: "applicationName",
      header: t("common.name"),
      meta: { label: t("common.name") },
    },
    {
      id: "subscriptionTier",
      accessorFn: (row) => row.subscriptionTier ?? "",
      filterFn: "faceted",
      header: t("applications.subscriptionTier"),
      meta: {
        label: t("applications.subscriptionTier"),
        filterVariant: "faceted",
      },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.subscriptionTier ?? "—"}
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
      cell: ({ row }) => (
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
                variant="destructive"
                onClick={() => setRemoving(row.original)}
              >
                {t("common.remove")}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      ),
    },
  ]

  const closeEnable = React.useCallback((next: boolean) => {
    setEnableOpen(next)
    if (!next) setAppId(undefined)
  }, [])
  const { requestOpenChange: requestEnableClose, discardDialog: enableDiscard } =
    useDirtyClose({ isDirty: Boolean(appId), onOpenChange: closeEnable })

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => setEnableOpen(true)}>
          <Plus />
          {t("organizations.enableApplication")}
        </Button>
      </div>
      <DataTable
        tableId="org-apps"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
      />

      <Dialog open={enableOpen} onOpenChange={requestEnableClose}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("organizations.enableApplication")}</DialogTitle>
          </DialogHeader>
          <ApplicationSelect
            value={appId}
            onChange={setAppId}
            className="w-full"
          />
          <DialogFooter>
            <Button variant="outline" onClick={() => requestEnableClose(false)}>
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => appId && enableMutation.mutate(appId)}
              disabled={!appId || enableMutation.isPending}
            >
              {enableMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : null}
              {t("common.confirm")}
            </Button>
          </DialogFooter>
          {enableDiscard}
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={Boolean(removing)}
        onOpenChange={(open) => !open && setRemoving(undefined)}
        title={t("organizations.enableApplication")}
        description={removing?.applicationName}
        confirmLabel={t("common.remove")}
        destructive
        loading={removeMutation.isPending}
        onConfirm={() =>
          removing?.applicationId &&
          removeMutation.mutate(removing.applicationId)
        }
      />
    </div>
  )
}

export function OrganizationDetailPage() {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const orgId = id as string
  const [editOpen, setEditOpen] = React.useState(false)

  const detailQuery = useQuery({
    queryKey: ["organizations", orgId],
    enabled: Boolean(orgId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}", {
          params: { path: { id: orgId } },
        })
      ),
  })

  const org = detailQuery.data

  return (
    <div className="space-y-6">
      <Link
        to="/organizations"
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" />
        {t("organizations.title")}
      </Link>

      {detailQuery.isLoading || !org ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <PageHeader
          title={org.name ?? "—"}
          description={org.code}
          actions={
            <Button variant="outline" onClick={() => setEditOpen(true)}>
              {t("common.edit")}
            </Button>
          }
        />
      )}

      <Tabs defaultValue="members">
        <TabsList>
          <TabsTrigger value="members">
            {t("organizations.members")}
          </TabsTrigger>
          <TabsTrigger value="invitations">
            {t("organizations.invitations")}
          </TabsTrigger>
          <TabsTrigger value="applications">
            {t("organizations.applications")}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="members" className="mt-4">
          <MembersTab orgId={orgId} />
        </TabsContent>
        <TabsContent value="invitations" className="mt-4">
          <InvitationsTab orgId={orgId} />
        </TabsContent>
        <TabsContent value="applications" className="mt-4">
          <ApplicationsTab orgId={orgId} />
        </TabsContent>
      </Tabs>

      {org ? (
        <OrganizationFormDialog
          open={editOpen}
          onOpenChange={setEditOpen}
          organization={org}
        />
      ) : null}
    </div>
  )
}
