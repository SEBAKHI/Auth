import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef, ColumnFiltersState } from "@tanstack/react-table"
import { MoreHorizontal, Plus, Send } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"
import { toast } from "sonner"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { Spinner } from "@authsystem/ui/spinner"
import { DatePicker, monthsFromNow } from "@authsystem/ui/common/date-picker"
import { PresetField, type Preset } from "@authsystem/ui/common/preset-field"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { DetailList } from "@authsystem/ui/common/detail-list"
import { LogoAvatar } from "@authsystem/ui/common/logo-avatar"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { ORGANIZATION_MEMBER_SORT_COLUMNS } from "../../lib/sortable-columns"
import { avatarColumn } from "@authsystem/ui/data-table/columns"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { Field, FieldError, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Switch } from "@authsystem/ui/switch"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { useDirtyClose } from "@authsystem/ui/hooks/use-dirty-close"
import {
  stringArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { useTabParam } from "@authsystem/ui/hooks/use-tab-param"
import { api } from "@authsystem/api/client"
import {
  collectAllPages,
  toSortParams,
  unwrap,
  toNumber,
} from "@authsystem/api/helpers"
import { decodeJwt } from "@authsystem/api/jwt"
import { getAccessToken } from "@authsystem/api/token-store"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { DEFAULT_PAGE_SIZE } from "@authsystem/api/constants"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime, fullName } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"
import { MemberAppRolesDialog } from "./member-app-roles-dialog"
import { OrganizationFormDialog } from "./organization-form-dialog"
import { TransferOwnershipDialog } from "./transfer-ownership-dialog"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

type OrganizationMembersUrlFilters = { roles: string[] }

const ORGANIZATION_MEMBERS_URL_OPTIONS = {
  namespace: "members",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: ORGANIZATION_MEMBER_SORT_COLUMNS,
  defaultSorting: [{ id: "joinedAt", desc: false }],
  filters: {
    roles: stringArrayUrlFilter({ param: "role" }),
  },
} satisfies ListUrlStateOptions<OrganizationMembersUrlFilters>

const ORGANIZATION_DETAIL_TABS = [
  "members",
  "invitations",
  "applications",
] as const

/**
 * Subscription tiers offered when enabling an application for an organization.
 * Free text on the wire, so the list stays open via the Custom choice — but the
 * field used to be a bare text box, which meant every operator invented their own
 * spelling and the dashboard's tier badges could never be grouped.
 */
const SUBSCRIPTION_TIERS: Preset[] = [
  { value: "free", label: "free" },
  { value: "standard", label: "standard" },
  { value: "pro", label: "pro" },
  { value: "enterprise", label: "enterprise" },
]

function MembersTab({
  orgId,
  userHref,
}: {
  orgId: string
  userHref?: (userId: string | undefined) => string | undefined
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const {
    pageIndex: page,
    pageSize,
    sorting,
    filters,
    setPageIndex: setPage,
    setPageSize,
    setSorting,
    setFilters,
  } = useListUrlState(ORGANIZATION_MEMBERS_URL_OPTIONS)
  const [removing, setRemoving] =
    React.useState<Schemas["OrganizationMemberDto"]>()
  const [managingRoles, setManagingRoles] =
    React.useState<Schemas["OrganizationMemberDto"]>()
  const [changingRole, setChangingRole] =
    React.useState<Schemas["OrganizationMemberDto"]>()
  const { sortBy, sortDirection } = toSortParams(sorting)
  const columnFilters: ColumnFiltersState = filters.roles.length
    ? [{ id: "roleName", value: filters.roles }]
    : []
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      roles:
        (next.find((filter) => filter.id === "roleName")?.value as
          | string[]
          | undefined) ?? [],
    })

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
    avatarColumn<Schemas["OrganizationMemberDto"]>({
      getSrc: (row) => row.profileImageUrl,
      getName: (row) =>
        row.fullName || fullName(row.firstName, row.lastName, row.email ?? ""),
    }),
    {
      id: "name",
      accessorFn: (row) =>
        row.fullName || fullName(row.firstName, row.lastName, row.email ?? ""),
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => {
        const { userId } = row.original
        const content = (
          <>
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
          </>
        )

        // Drill-down exists only where the host app has a user admin route.
        return (
          <RecordLink
            href={userHref?.(userId)}
            className="text-start"
          >
            {content}
          </RecordLink>
        )
      },
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
          {formatDateTime(row.original.joinedAt)}
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
                <DropdownMenuItem onClick={() => setChangingRole(row.original)}>
                  {t("organizations.changeRole")}
                </DropdownMenuItem>
                <DropdownMenuItem
                  onClick={() => setManagingRoles(row.original)}
                >
                  {t("organizations.manageAppRoles")}
                </DropdownMenuItem>
                <DropdownMenuItem
                  variant="destructive"
                  onClick={() => setRemoving(row.original)}
                >
                  {t("organizations.removeMember")}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
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
        enableRowDetail={false}
        onExportAll={exportAll}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        sorting={sorting}
        onSortingChange={setSorting}
        pagination={{
          pageIndex: page,
          pageSize,
          pageCount: toNumber(query.data?.totalPages),
          totalCount: toNumber(query.data?.totalCount),
          onPageChange: setPage,
          onPageSizeChange: setPageSize,
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
      {managingRoles ? (
        <MemberAppRolesDialog
          open={Boolean(managingRoles)}
          onOpenChange={(open) => !open && setManagingRoles(undefined)}
          orgId={orgId}
          member={managingRoles}
        />
      ) : null}
      {changingRole ? (
        <ChangeMemberRoleDialog
          orgId={orgId}
          member={changingRole}
          onClose={() => setChangingRole(undefined)}
        />
      ) : null}
    </>
  )
}

/** Changes a member's organization-level role (PUT members/{userId}/role). */
function ChangeMemberRoleDialog({
  orgId,
  member,
  onClose,
}: {
  orgId: string
  member: Schemas["OrganizationMemberDto"]
  onClose: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [roleId, setRoleId] = React.useState<string | undefined>(
    member.roleId as string | undefined
  )

  const rolesQuery = useQuery({
    queryKey: ["roles", "all"],
    queryFn: () => unwrap(api.GET("/api/v1/Roles", { params: { query: {} } })),
  })

  const mutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT(
        "/api/v1/Organizations/{orgId}/members/{userId}/role",
        {
          params: { path: { orgId, userId: member.userId as string } },
          body: { roleId: roleId ?? "" },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["org-members", orgId] })
      toast.success(t("organizations.roleUpdated"))
      onClose()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("organizations.changeRole")}</DialogTitle>
        </DialogHeader>
        <Field>
          <FieldLabel htmlFor="member-role">{t("common.role")}</FieldLabel>
          <Select value={roleId} onValueChange={setRoleId}>
            <SelectTrigger id="member-role" className="w-full">
              <SelectValue placeholder={t("common.role")} />
            </SelectTrigger>
            <SelectContent>
              {/* Membership role is organization-level (no application). */}
              <SelectGroup>
                {(rolesQuery.data ?? [])
                  .filter((role) => role.id && !role.applicationId)
                  .map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                    </SelectItem>
                  ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!roleId || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
  // Only complain once something has been typed, so the field does not open
  // in an error state — but do explain why Confirm stays disabled.
  const emailInvalid = email.length > 0 && !EMAIL_RE.test(email)

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
        <FieldGroup>
          <Field data-invalid={emailInvalid}>
            <FieldLabel htmlFor="invite-email">
              {t("organizations.inviteEmail")}
            </FieldLabel>
            <Input
              id="invite-email"
              type="email"
              aria-invalid={emailInvalid}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="name@example.com"
              dir="ltr"
            />
            {emailInvalid ? (
              <FieldError>{t("validation.email")}</FieldError>
            ) : null}
          </Field>
          <Field>
            <FieldLabel htmlFor="invite-role">{t("common.role")}</FieldLabel>
            <Select value={roleId} onValueChange={setRoleId}>
              <SelectTrigger id="invite-role" className="w-full">
                <SelectValue placeholder={t("common.role")} />
              </SelectTrigger>
              <SelectContent>
                {/* The invited role becomes the org membership role, so only
                    organization-level roles (no application) are valid here. */}
                <SelectGroup>
                  {(rolesQuery.data ?? [])
                    .filter((role) => role.id && !role.applicationId)
                    .map((role) => (
                      <SelectItem key={role.id} value={role.id as string}>
                        {role.name}
                      </SelectItem>
                    ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={() => requestOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!valid || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
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
          {formatDateTime(row.original.expiresAt)}
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
            <Send data-icon="inline-start" />
            {t("organizations.resendInvite")}
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Button onClick={() => setInviteOpen(true)}>
          <Plus data-icon="inline-start" />
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
      {inviteOpen ? (
        <InviteDialog orgId={orgId} open onOpenChange={setInviteOpen} />
      ) : null}
    </div>
  )
}

function ApplicationsTab({
  orgId,
  applicationHref,
}: {
  orgId: string
  applicationHref?: (applicationId: string | undefined) => string | undefined
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [enableOpen, setEnableOpen] = React.useState(false)
  const [appId, setAppId] = React.useState<string>()
  const [tier, setTier] = React.useState("")
  const [expiresAt, setExpiresAt] = React.useState("")
  const [editingApp, setEditingApp] =
    React.useState<Schemas["OrganizationApplicationDto"]>()
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

  /**
   * What this organization may actually enable: switched on, open to everyone,
   * and not already enabled here.
   *
   * Not the platform-wide application list. That one needs `applications:read`,
   * which an organization administrator has no reason to hold — without it the
   * picker simply came up empty with nothing to explain why — and it includes
   * applications restricted to their own invitation list, which no organization
   * can enable at all.
   */
  const availableQuery = useQuery({
    queryKey: ["org-apps", orgId, "available"],
    enabled: enableOpen,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/applications/available", {
          params: { path: { id: orgId } },
        })
      ),
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({
      queryKey: ["org-apps", orgId, "available"],
    })
    return queryClient.invalidateQueries({ queryKey: ["org-apps", orgId] })
  }

  const enableMutation = useMutation({
    mutationFn: async (applicationId: string) => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{id}/applications",
        {
          params: { path: { id: orgId } },
          body: {
            applicationId,
            subscriptionTier: tier.trim() || null,
            expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
          },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("organizations.appEnabled"))
      setEnableOpen(false)
      setAppId(undefined)
      setTier("")
      setExpiresAt("")
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
    avatarColumn<Schemas["OrganizationApplicationDto"]>({
      getSrc: (row) => row.applicationLogoUrl,
      getName: (row) => row.applicationName,
      fit: "contain",
    }),
    {
      accessorKey: "applicationName",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => {
        const { applicationId } = row.original

        // Drill-down exists only where the host app has an app admin route.
        return (
          <RecordLink
            href={applicationHref?.(applicationId)}
            className="text-start font-medium"
          >
            <span className="truncate">{row.original.applicationName}</span>
          </RecordLink>
        )
      },
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
      id: "assignedUserCount",
      accessorFn: (row) => toNumber(row.assignedUserCount),
      header: t("organizations.assignedUserCount"),
      meta: { label: t("organizations.assignedUserCount") },
      cell: ({ row }) => (
        <span className="text-sm tabular-nums">
          {toNumber(row.original.assignedUserCount)}
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
                <DropdownMenuItem onClick={() => setEditingApp(row.original)}>
                  {t("organizations.editSubscription")}
                </DropdownMenuItem>
                <DropdownMenuItem
                  variant="destructive"
                  onClick={() => setRemoving(row.original)}
                >
                  {t("common.remove")}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      ),
    },
  ]

  const closeEnable = React.useCallback((next: boolean) => {
    setEnableOpen(next)
    if (!next) {
      setAppId(undefined)
      setTier("")
      setExpiresAt("")
    }
  }, [])
  const {
    requestOpenChange: requestEnableClose,
    discardDialog: enableDiscard,
  } = useDirtyClose({
    isDirty: Boolean(appId) || Boolean(tier) || Boolean(expiresAt),
    onOpenChange: closeEnable,
  })

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Button onClick={() => setEnableOpen(true)}>
          <Plus data-icon="inline-start" />
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
        enableRowDetail={false}
      />

      <Dialog open={enableOpen} onOpenChange={requestEnableClose}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("organizations.enableApplication")}</DialogTitle>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="enable-app-application">
                {t("common.application")}
              </FieldLabel>
              <ApplicationSelect
                id="enable-app-application"
                value={appId}
                onChange={setAppId}
                className="w-full"
                options={(availableQuery.data ?? []).map((app) => ({
                  id: app.applicationId,
                  name: app.name,
                }))}
                loading={availableQuery.isLoading}
              />
            </Field>
            <Field>
              <FieldLabel id="enable-app-tier-label" htmlFor="enable-app-tier">
                {t("applications.subscriptionTier")}
              </FieldLabel>
              <PresetField
                aria-labelledby="enable-app-tier-label"
                presets={SUBSCRIPTION_TIERS}
                value={tier}
                onChange={setTier}
              >
                {({ value, onChange }) => (
                  <Input
                    id="enable-app-tier"
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    placeholder="pro"
                    dir="ltr"
                  />
                )}
              </PresetField>
            </Field>
            <Field>
              <FieldLabel htmlFor="enable-app-expires">
                {t("common.expiresAt")}
              </FieldLabel>
              <DatePicker
                id="enable-app-expires"
                value={expiresAt}
                onChange={(value) => setExpiresAt(value ?? "")}
                minDate={new Date()}
                maxDate={monthsFromNow(10)}
                placeholder={t("common.never")}
              />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" onClick={() => requestEnableClose(false)}>
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => appId && enableMutation.mutate(appId)}
              disabled={!appId || enableMutation.isPending}
            >
              {enableMutation.isPending ? <Spinner /> : null}
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
      {editingApp ? (
        <EditOrgAppDialog
          orgId={orgId}
          app={editingApp}
          onClose={() => setEditingApp(undefined)}
        />
      ) : null}
    </div>
  )
}

/** Edits an enabled application's subscription tier, expiry, and active state. */
function EditOrgAppDialog({
  orgId,
  app,
  onClose,
}: {
  orgId: string
  app: Schemas["OrganizationApplicationDto"]
  onClose: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [tier, setTier] = React.useState(app.subscriptionTier ?? "")
  const [expiresAt, setExpiresAt] = React.useState(
    app.expiresAt ? app.expiresAt.slice(0, 10) : ""
  )
  const [isActive, setIsActive] = React.useState(app.isActive ?? true)

  const mutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT(
        "/api/v1/Organizations/{id}/applications/{applicationId}",
        {
          params: {
            path: { id: orgId, applicationId: app.applicationId as string },
          },
          body: {
            subscriptionTier: tier.trim() || null,
            expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
            isActive,
          },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["org-apps", orgId] })
      toast.success(t("organizations.appUpdated"))
      onClose()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("organizations.editSubscription")}</DialogTitle>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel id="edit-sub-tier-label" htmlFor="edit-sub-tier">
              {t("applications.subscriptionTier")}
            </FieldLabel>
            <PresetField
              aria-labelledby="edit-sub-tier-label"
              presets={SUBSCRIPTION_TIERS}
              value={tier}
              onChange={setTier}
            >
              {({ value, onChange }) => (
                <Input
                  id="edit-sub-tier"
                  value={value}
                  onChange={(event) => onChange(event.target.value)}
                  placeholder="pro"
                  dir="ltr"
                />
              )}
            </PresetField>
          </Field>
          <Field>
            <FieldLabel htmlFor="edit-sub-expires">
              {t("common.expiresAt")}
            </FieldLabel>
            <DatePicker
              id="edit-sub-expires"
              value={expiresAt}
              onChange={(value) => setExpiresAt(value ?? "")}
              minDate={new Date()}
              maxDate={monthsFromNow(10)}
              placeholder={t("common.never")}
            />
          </Field>
          <Field
            orientation="horizontal"
            className="justify-between rounded-lg border p-3"
          >
            <FieldLabel htmlFor="edit-sub-active" className="font-normal">
              {t("common.active")}
            </FieldLabel>
            <Switch
              id="edit-sub-active"
              checked={isActive}
              onCheckedChange={setIsActive}
            />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function OrganizationDetailPage({
  userHref,
  applicationHref,
  canManagePlatform = false,
}: {
  /** Builds the member drill-down route; omit where the host app has none. */
  userHref?: (userId: string | undefined) => string | undefined
  /** Builds the application drill-down route; omit where the host app has none. */
  applicationHref?: (applicationId: string | undefined) => string | undefined
  /** Platform recovery capability supplied only by the administrative host. */
  canManagePlatform?: boolean
} = {}) {
  const { t } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const orgId = id as string
  const queryClient = useQueryClient()
  const [activeTab, setActiveTab] = useTabParam(ORGANIZATION_DETAIL_TABS)
  const [editOpen, setEditOpen] = React.useState(false)
  const [transferOpen, setTransferOpen] = React.useState(false)

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
  usePageBreadcrumb(org?.name)

  // Ownership transfer is a self-service, owner-only action: surface it only
  // when the viewer is the organization's actual owner (from the access token's
  // subject). Platform-admin recovery transfers go through the API directly.
  const currentUserId = React.useMemo(() => {
    const token = getAccessToken()
    return token ? decodeJwt(token)?.sub : undefined
  }, [])
  const isOwner = Boolean(
    org?.ownerId && currentUserId && org.ownerId === currentUserId
  )
  const canTransferOwnership = isOwner || canManagePlatform

  return (
    <div className="flex flex-col gap-6">
      {detailQuery.isLoading || !org ? (
        <Skeleton className="h-20 w-full" />
      ) : (
        <>
          <PageHeader
            title={org.name ?? "—"}
            description={org.code}
            leading={
              <LogoAvatar
                src={org.logoUrl}
                name={org.name}
                canEdit
                successMessage={t("organizations.updated")}
                invalidate={() => {
                  void queryClient.invalidateQueries({
                    queryKey: ["organizations", orgId],
                  })
                  void queryClient.invalidateQueries({
                    queryKey: ["organizations"],
                  })
                }}
                persist={async (logoKey) => {
                  const { error } = await api.PUT(
                    "/api/v1/Organizations/{id}",
                    {
                      params: { path: { id: orgId } },
                      body: {
                        name: org.name ?? "",
                        contactEmail: org.contactEmail ?? "",
                        website: org.website ?? null,
                        logoUrl: logoKey,
                        description: org.description ?? null,
                        isActive: org.isActive ?? true,
                      },
                    }
                  )
                  if (error) throw error
                }}
              />
            }
            actions={
              <div className="flex items-center gap-2">
                {canTransferOwnership ? (
                  <Button
                    variant="outline"
                    onClick={() => setTransferOpen(true)}
                  >
                    {t("organizations.transferOwnership")}
                  </Button>
                ) : null}
                <Button variant="outline" onClick={() => setEditOpen(true)}>
                  {t("common.edit")}
                </Button>
              </div>
            }
          />
          <DetailList
            items={[
              {
                label: t("common.description"),
                value: org.description,
                fullWidth: true,
              },
              {
                label: t("common.status"),
                value: (
                  <Badge variant={org.isActive ? "default" : "secondary"}>
                    {org.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                ),
              },
              { label: t("organizations.website"), value: org.website },
              { label: t("applications.logoUrl"), value: org.logoUrl },
              {
                label: t("applications.contactEmail"),
                value: org.contactEmail,
              },
              {
                label: t("organizations.owner"),
                value: org.ownerName || org.ownerEmail,
              },
              {
                label: t("organizations.memberCount"),
                value: toNumber(org.memberCount),
              },
              {
                label: t("organizations.enabledAppCount"),
                value: toNumber(org.enabledAppCount),
              },
              {
                label: t("common.createdAt"),
                value: formatDateTime(org.createdAt),
              },
              { label: t("common.createdBy"), value: org.createdByName },
              {
                label: t("common.modifiedAt"),
                value: formatDateTime(org.modifiedAt),
              },
              { label: t("common.modifiedBy"), value: org.modifiedByName },
            ]}
          />
        </>
      )}

      <Tabs value={activeTab} onValueChange={setActiveTab}>
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
          <MembersTab orgId={orgId} userHref={userHref} />
        </TabsContent>
        <TabsContent value="invitations" className="mt-4">
          <InvitationsTab orgId={orgId} />
        </TabsContent>
        <TabsContent value="applications" className="mt-4">
          <ApplicationsTab orgId={orgId} applicationHref={applicationHref} />
        </TabsContent>
      </Tabs>

      {org ? (
        <OrganizationFormDialog
          open={editOpen}
          onOpenChange={setEditOpen}
          organization={org}
        />
      ) : null}
      {canTransferOwnership ? (
        <TransferOwnershipDialog
          orgId={orgId}
          ownerId={org?.ownerId}
          platformScope={!isOwner && canManagePlatform}
          open={transferOpen}
          onOpenChange={setTransferOpen}
          onTransferred={() => {
            void queryClient.invalidateQueries({
              queryKey: ["organizations", orgId],
            })
            void queryClient.invalidateQueries({ queryKey: ["organizations"] })
          }}
        />
      ) : null}
    </div>
  )
}
