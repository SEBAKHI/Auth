import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus, ShieldCheck } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
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
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { PresetField } from "@authsystem/ui/common/preset-field"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  DEFAULT_GRACE_PERIOD_MINUTES,
  MIN_GRACE_PERIOD_MINUTES,
  toGracePeriod,
  useGracePeriodPresets,
} from "@/lib/presets"
import { FieldConstraints } from "@authsystem/ui/common/field-constraints"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@authsystem/api/errors"
import { daysUntil, formatDateTime } from "@authsystem/ui/format"
import { useSearchHandoff } from "@authsystem/ui/hooks/use-search-query"
import type { Schemas } from "@authsystem/api/types"
import { ApiKeyCreateDialog } from "./api-key-create-dialog"
import { Spinner } from "@authsystem/ui/spinner"

type ApiKeyDto = Schemas["ApiKeyDto"]

/**
 * Matches the server's default credential-expiry horizon, so the rows this page
 * shows are the rows the dashboard counted.
 */
const EXPIRY_SOON_DAYS = 14

type ExpiryState = "expired" | "soon" | "later" | "none"

function expiryState(iso: string | null | undefined): ExpiryState {
  const remaining = daysUntil(iso)
  if (remaining === null) return "none"
  if (remaining < 0) return "expired"
  return remaining <= EXPIRY_SOON_DAYS ? "soon" : "later"
}

function ValidateApiKeyDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const [value, setValue] = React.useState("")

  const mutation = useMutation({
    mutationFn: (apiKey: string) =>
      unwrap(api.POST("/api/v1/ApiKeys/validate", { body: { apiKey } })),
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  React.useEffect(() => {
    if (open) {
      setValue("")
      mutation.reset()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const result = mutation.data

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("apiKeys.validateTitle")}</DialogTitle>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="validate-api-key">
              {t("apiKeys.keyLabel")}
            </FieldLabel>
            {/* Pasted key material — pinned LTR to match the dialog that issues
                it, so the same string is not read two different ways. */}
            <Input
              id="validate-api-key"
              dir="ltr"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder={t("apiKeys.validatePlaceholder")}
              className="font-mono text-xs"
            />
          </Field>
          {result ? (
            <div className="rounded-lg border p-3 text-sm">
              <Badge variant={result.active ? "default" : "destructive"}>
                {result.active ? t("common.active") : t("common.inactive")}
              </Badge>
              {result.active ? (
                <p className="mt-2 text-muted-foreground">
                  {result.name} · {result.environment}
                </p>
              ) : null}
            </div>
          ) : null}
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.close")}
          </Button>
          <Button
            onClick={() => value && mutation.mutate(value)}
            disabled={!value || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("apiKeys.validate")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function ApiKeysPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  // undefined now means "every application", not "nothing chosen": the table
  // renders on arrival instead of waiting for a picker the reader may not even be
  // allowed to load (the picker needs applications:read; this page does not).
  const [applicationId, setApplicationId] = React.useState<string>()
  const initialExpiry = useSearchHandoff("expiry")
  const [createOpen, setCreateOpen] = React.useState(false)
  const [validateOpen, setValidateOpen] = React.useState(false)
  const [revealValue, setRevealValue] = React.useState<string>()
  const [revokeKey, setRevokeKey] = React.useState<ApiKeyDto | undefined>()
  const [revokeReason, setRevokeReason] = React.useState("")
  const [rotateKey, setRotateKey] = React.useState<ApiKeyDto | undefined>()
  const [grace, setGrace] = React.useState("60")
  const gracePresets = useGracePeriodPresets()

  const canCreate = hasPermission(PERMISSIONS.apiKeys.create)
  const canRevoke = hasPermission(PERMISSIONS.apiKeys.revoke)
  const canRotate = hasPermission(PERMISSIONS.apiKeys.rotate)
  const canValidate = hasPermission(PERMISSIONS.apiKeys.validate)
  const hasRowActions = canRevoke || canRotate

  const query = useQuery({
    queryKey: ["api-keys", { applicationId }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/ApiKeys", {
          params: { query: applicationId ? { applicationId } : {} },
        })
      ),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["api-keys"] })

  const revokeMutation = useMutation({
    mutationFn: async (input: { id: string; reason: string }) => {
      const { error } = await api.POST("/api/v1/ApiKeys/{id}/revoke", {
        params: { path: { id: input.id } },
        body: { reason: input.reason || null },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("apiKeys.revoked"))
      setRevokeKey(undefined)
      setRevokeReason("")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const rotateMutation = useMutation({
    mutationFn: async (input: { id: string; gracePeriodMinutes: number }) =>
      unwrap(
        api.POST("/api/v1/ApiKeys/{id}/rotate", {
          params: { path: { id: input.id } },
          body: { gracePeriodMinutes: input.gracePeriodMinutes },
        })
      ),
    onSuccess: (data) => {
      void invalidate()
      toast.success(t("apiKeys.rotated"))
      setRotateKey(undefined)
      if (data?.newApiKey) setRevealValue(data.newApiKey)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<ApiKeyDto, unknown>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate font-mono text-xs text-muted-foreground">
            {/* The ellipsis is neutral, so in an RTL paragraph it took the
                paragraph level and rendered before the prefix. */}
            <bdi dir="ltr">{row.original.keyPrefix}…</bdi>
          </p>
        </div>
      ),
    },
    {
      accessorKey: "environment",
      filterFn: "faceted",
      header: t("apiKeys.environment"),
      meta: { label: t("apiKeys.environment"), filterVariant: "faceted" },
    },
    {
      id: "status",
      accessorFn: (row) => (row.isRevoked ? "revoked" : "active"),
      filterFn: "faceted",
      header: t("common.status"),
      meta: {
        label: t("common.status"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "active", label: t("common.active") },
          { value: "revoked", label: t("common.revoked") },
        ],
      },
      cell: ({ row }) => (
        <Badge variant={row.original.isRevoked ? "destructive" : "default"}>
          {row.original.isRevoked ? t("common.revoked") : t("common.active")}
        </Badge>
      ),
    },
    {
      id: "expiresAt",
      // The accessor stays the raw instant so sorting, CSV export and the row
      // detail sheet all show a date; the bucket exists only inside the predicate.
      accessorFn: (row) => row.expiresAt ?? "",
      filterFn: (row, _columnId, selected: string[]) =>
        selected.includes(expiryState(row.original.expiresAt)),
      header: t("common.expiresAt"),
      meta: {
        label: t("common.expiresAt"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "expired", label: t("apiKeys.expired") },
          { value: "soon", label: t("apiKeys.expiringSoon") },
          { value: "later", label: t("apiKeys.expiresLater") },
          { value: "none", label: t("apiKeys.noExpiry") },
        ],
      },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.expiresAt
            ? formatDateTime(row.original.expiresAt)
            : t("apiKeys.noExpiry")}
        </span>
      ),
    },
    {
      id: "lastUsedAt",
      accessorFn: (row) => row.lastUsedAt ?? "",
      header: t("common.lastUsed"),
      meta: { label: t("common.lastUsed") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.lastUsedAt
            ? formatDateTime(row.original.lastUsedAt)
            : t("common.never")}
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
              const key = row.original
              if (key.isRevoked) return null
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
                        {canRotate ? (
                          <DropdownMenuItem onClick={() => setRotateKey(key)}>
                            {t("apiKeys.rotate")}
                          </DropdownMenuItem>
                        ) : null}
                      </DropdownMenuGroup>
                      {canRevoke ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuGroup>
                            <DropdownMenuItem
                              variant="destructive"
                              onClick={() => setRevokeKey(key)}
                            >
                              {t("apiKeys.revoke")}
                            </DropdownMenuItem>
                          </DropdownMenuGroup>
                        </>
                      ) : null}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )
            },
          } satisfies ColumnDef<ApiKeyDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <PageHeader
        title={t("apiKeys.title")}
        description={t("apiKeys.subtitle")}
        actions={
          <div className="flex items-center gap-2">
            {canValidate ? (
              <Button variant="outline" onClick={() => setValidateOpen(true)}>
                <ShieldCheck data-icon="inline-start" />
                {t("apiKeys.validate")}
              </Button>
            ) : null}
            {canCreate ? (
              <Button
                onClick={() => setCreateOpen(true)}
                disabled={!applicationId}
              >
                <Plus data-icon="inline-start" />
                {t("apiKeys.newKey")}
              </Button>
            ) : null}
          </div>
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
        tableId="api-keys"
        globalSearch
        columns={columns}
        data={query.data ?? []}
        // A key that slipped past its date while the alert was up is the most
        // urgent row here, so the link that was meant to surface it must not
        // filter it out: "expiring soon" opens on soon AND already expired.
        initialColumnFilters={
          initialExpiry === "soon"
            ? [{ id: "expiresAt", value: ["soon", "expired"] }]
            : []
        }
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
      />

      {applicationId ? (
        <ApiKeyCreateDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          applicationId={applicationId}
          onCreated={setRevealValue}
        />
      ) : null}

      <ValidateApiKeyDialog
        open={validateOpen}
        onOpenChange={setValidateOpen}
      />

      <SecretRevealDialog
        open={Boolean(revealValue)}
        onOpenChange={(open) => !open && setRevealValue(undefined)}
        title={t("apiKeys.secretOnceTitle")}
        description={t("apiKeys.secretOnceBody")}
        value={revealValue ?? ""}
      />

      <ConfirmDialog
        open={Boolean(revokeKey)}
        onOpenChange={(open) => {
          if (!open) {
            setRevokeKey(undefined)
            setRevokeReason("")
          }
        }}
        title={t("apiKeys.revokeTitle")}
        description={t("apiKeys.revokeBody")}
        confirmLabel={t("apiKeys.revoke")}
        destructive
        loading={revokeMutation.isPending}
        onConfirm={() =>
          revokeKey?.id &&
          revokeMutation.mutate({ id: revokeKey.id, reason: revokeReason })
        }
      >
        <Field>
          <FieldLabel htmlFor="revoke-reason">
            {t("apiKeys.revokeReason")}
          </FieldLabel>
          <Input
            id="revoke-reason"
            value={revokeReason}
            onChange={(e) => setRevokeReason(e.target.value)}
            placeholder={t("apiKeys.revokeReasonPlaceholder")}
          />
        </Field>
      </ConfirmDialog>

      <ConfirmDialog
        open={Boolean(rotateKey)}
        onOpenChange={(open) => !open && setRotateKey(undefined)}
        title={t("apiKeys.rotateTitle")}
        description={t("apiKeys.rotateBody")}
        confirmLabel={t("apiKeys.rotate")}
        loading={rotateMutation.isPending}
        onConfirm={() =>
          rotateKey?.id &&
          rotateMutation.mutate({
            id: rotateKey.id,
            gracePeriodMinutes: toGracePeriod(grace),
          })
        }
      >
        <Field>
          <FieldLabel htmlFor="grace">{t("apiKeys.gracePeriod")}</FieldLabel>
          <PresetField presets={gracePresets} value={grace} onChange={setGrace}>
            {({ value, onChange }) => (
              <Input
                id="grace"
                type="number"
                min={MIN_GRACE_PERIOD_MINUTES}
                value={value}
                onChange={(event) => onChange(event.target.value)}
              />
            )}
          </PresetField>
          <FieldDescription>{t("apiKeys.gracePeriodHint")}</FieldDescription>
          <FieldConstraints
            min={MIN_GRACE_PERIOD_MINUTES}
            defaultValue={DEFAULT_GRACE_PERIOD_MINUTES}
          />
        </Field>
      </ConfirmDialog>
    </div>
  )
}
