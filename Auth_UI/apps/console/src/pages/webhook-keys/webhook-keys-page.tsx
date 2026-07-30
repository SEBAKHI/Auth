import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal, Plus, ShieldCheck } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ApplicationSelect } from "@astoom/ui/common/application-select"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { PageHeader } from "@astoom/ui/common/page-header"
import { SecretRevealDialog } from "@astoom/ui/common/secret-reveal-dialog"
import { DataTable } from "@astoom/ui/data-table/data-table"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { PresetField } from "@astoom/ui/common/preset-field"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { toGracePeriod, useGracePeriodPresets } from "@/lib/presets"
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { getErrorMessage } from "@astoom/api/errors"
import { formatDateTime } from "@astoom/ui/format"
import type { Schemas } from "@astoom/api/types"
import { WebhookKeyCreateDialog } from "./webhook-key-create-dialog"
import { Spinner } from "@astoom/ui/spinner"

type WebhookKeyDto = Schemas["WebhookKeyDto"]

function ValidateWebhookKeyDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const [value, setValue] = React.useState("")

  const mutation = useMutation({
    mutationFn: (webhookKey: string) =>
      unwrap(
        api.POST("/api/v1/WebhookKeys/validate", { body: { webhookKey } })
      ),
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
          <DialogTitle>{t("webhookKeys.validate")}</DialogTitle>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="validate-webhook-key">
              {t("webhookKeys.keyLabel")}
            </FieldLabel>
            <Input
              id="validate-webhook-key"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder={t("webhookKeys.validatePlaceholder")}
              className="font-mono text-xs"
            />
          </Field>
          {result ? (
            <div className="rounded-lg border p-3 text-sm">
              <Badge variant={result.active ? "default" : "destructive"}>
                {result.active ? t("common.active") : t("common.inactive")}
              </Badge>
              {result.active ? (
                <p className="mt-2 truncate text-muted-foreground">
                  {result.name} · {result.targetUrl}
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
            {t("webhookKeys.validate")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function WebhookKeysPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [applicationId, setApplicationId] = React.useState<string>()
  const [createOpen, setCreateOpen] = React.useState(false)
  const [validateOpen, setValidateOpen] = React.useState(false)
  const [revealValue, setRevealValue] = React.useState<string>()
  const [revokeKey, setRevokeKey] = React.useState<WebhookKeyDto | undefined>()
  const [revokeReason, setRevokeReason] = React.useState("")
  const [rotateKey, setRotateKey] = React.useState<WebhookKeyDto | undefined>()
  const [grace, setGrace] = React.useState("60")
  const gracePresets = useGracePeriodPresets()

  const canCreate = hasPermission(PERMISSIONS.webhookKeys.create)
  const canRevoke = hasPermission(PERMISSIONS.webhookKeys.revoke)
  const canRotate = hasPermission(PERMISSIONS.webhookKeys.rotate)
  const canValidate = hasPermission(PERMISSIONS.webhookKeys.validate)
  const hasRowActions = canRevoke || canRotate

  const query = useQuery({
    queryKey: ["webhook-keys", { applicationId }],
    enabled: Boolean(applicationId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/WebhookKeys", {
          params: { query: { applicationId: applicationId as string } },
        })
      ),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["webhook-keys"] })

  const revokeMutation = useMutation({
    mutationFn: async (input: { id: string; reason: string }) => {
      const { error } = await api.POST("/api/v1/WebhookKeys/{id}/revoke", {
        params: { path: { id: input.id } },
        body: { reason: input.reason || null },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("webhookKeys.revoked"))
      setRevokeKey(undefined)
      setRevokeReason("")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const rotateMutation = useMutation({
    mutationFn: (input: { id: string; gracePeriodMinutes: number }) =>
      unwrap(
        api.POST("/api/v1/WebhookKeys/{id}/rotate", {
          params: { path: { id: input.id } },
          body: { gracePeriodMinutes: input.gracePeriodMinutes },
        })
      ),
    onSuccess: (data) => {
      void invalidate()
      toast.success(t("webhookKeys.rotated"))
      setRotateKey(undefined)
      if (data?.newWebhookKey) setRevealValue(data.newWebhookKey)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<WebhookKeyDto, unknown>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: { label: t("common.name") },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate font-mono text-xs text-muted-foreground">
            {row.original.keyPrefix}…
          </p>
        </div>
      ),
    },
    {
      id: "targetUrl",
      accessorFn: (row) => row.targetUrl ?? "",
      header: t("webhookKeys.targetUrl"),
      meta: { label: t("webhookKeys.targetUrl") },
      cell: ({ row }) => (
        <span className="block max-w-[220px] truncate text-sm text-muted-foreground">
          {row.original.targetUrl}
        </span>
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
                            {t("webhookKeys.rotate")}
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
                              {t("webhookKeys.revoke")}
                            </DropdownMenuItem>
                          </DropdownMenuGroup>
                        </>
                      ) : null}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )
            },
          } satisfies ColumnDef<WebhookKeyDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("webhookKeys.title")}
        description={t("webhookKeys.subtitle")}
        actions={
          <div className="flex items-center gap-2">
            {canValidate ? (
              <Button variant="outline" onClick={() => setValidateOpen(true)}>
                <ShieldCheck data-icon="inline-start" />
                {t("webhookKeys.validate")}
              </Button>
            ) : null}
            {canCreate ? (
              <Button
                onClick={() => setCreateOpen(true)}
                disabled={!applicationId}
              >
                <Plus data-icon="inline-start" />
                {t("webhookKeys.newKey")}
              </Button>
            ) : null}
          </div>
        }
      />

      <div className="max-w-xs">
        <ApplicationSelect
          value={applicationId}
          onChange={setApplicationId}
          className="w-full"
        />
      </div>

      {applicationId ? (
        <DataTable
          tableId="webhook-keys"
          globalSearch
          columns={columns}
          data={query.data ?? []}
          isLoading={query.isLoading}
          error={query.isError ? query.error : undefined}
          onRetry={() => query.refetch()}
        />
      ) : (
        <p className="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">
          {t("common.selectApplication")}
        </p>
      )}

      {applicationId ? (
        <WebhookKeyCreateDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          applicationId={applicationId}
          onCreated={setRevealValue}
        />
      ) : null}

      <ValidateWebhookKeyDialog
        open={validateOpen}
        onOpenChange={setValidateOpen}
      />

      <SecretRevealDialog
        open={Boolean(revealValue)}
        onOpenChange={(open) => !open && setRevealValue(undefined)}
        title={t("webhookKeys.secretOnceTitle")}
        description={t("webhookKeys.secretOnceBody")}
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
        title={t("webhookKeys.revokeTitle")}
        description={t("webhookKeys.revokeBody")}
        confirmLabel={t("webhookKeys.revoke")}
        destructive
        loading={revokeMutation.isPending}
        onConfirm={() =>
          revokeKey?.id &&
          revokeMutation.mutate({ id: revokeKey.id, reason: revokeReason })
        }
      >
        <Field>
          <FieldLabel htmlFor="wh-revoke-reason">
            {t("apiKeys.revokeReason")}
          </FieldLabel>
          <Input
            id="wh-revoke-reason"
            value={revokeReason}
            onChange={(e) => setRevokeReason(e.target.value)}
            placeholder={t("apiKeys.revokeReasonPlaceholder")}
          />
        </Field>
      </ConfirmDialog>

      <ConfirmDialog
        open={Boolean(rotateKey)}
        onOpenChange={(open) => !open && setRotateKey(undefined)}
        title={t("webhookKeys.rotateTitle")}
        description={t("webhookKeys.rotateBody")}
        confirmLabel={t("webhookKeys.rotate")}
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
          <FieldLabel htmlFor="wh-grace">
            {t("webhookKeys.gracePeriod")}
          </FieldLabel>
          <PresetField presets={gracePresets} value={grace} onChange={setGrace}>
            {({ value, onChange }) => (
              <Input
                id="wh-grace"
                type="number"
                min={0}
                value={value}
                onChange={(event) => onChange(event.target.value)}
              />
            )}
          </PresetField>
          <FieldDescription>{t("webhookKeys.gracePeriodHint")}</FieldDescription>
        </Field>
      </ConfirmDialog>
    </div>
  )
}
