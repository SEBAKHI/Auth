import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnFiltersState, SortingState } from "@tanstack/react-table"
import { Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Button } from "@authsystem/ui/button"
import { PresetField } from "@authsystem/ui/common/preset-field"
import { Field, FieldDescription, FieldLabel } from "@authsystem/ui/field"
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
import type { Schemas } from "@authsystem/api/types"
import { WebhookKeyCreateDialog } from "./webhook-key-create-dialog"
import {
  useWebhookKeyColumns,
  type WebhookKeyColumnId,
} from "./webhook-key-columns"
import { revokeReasonOnlyWhenGiven } from "../credential-key-rows"

type WebhookKeyDto = Schemas["WebhookKeyDto"]

export interface WebhookKeysPanelProps {
  /** The application whose keys are listed and created; empty lists every application. */
  applicationId: string
  /**
   * What sits above the table: the page's header and picker, or the tab's line.
   * Receives the create button, or null when the reader may not create.
   */
  header: (createButton: React.ReactNode) => React.ReactNode
  tableId: string
  defaultHidden?: readonly WebhookKeyColumnId[]
  fillHeight?: boolean
  search: string
  onSearchChange: (value: string) => void
  sorting: SortingState
  onSortingChange: (value: SortingState) => void
  columnFilters: ColumnFiltersState
  onColumnFiltersChange: (value: ColumnFiltersState) => void
}

/**
 * The webhook-key table, one definition for every place that lists webhook keys: the
 * main-menu page across applications, and an application's own tab (OI-264).
 * Each surface keeps only what is its own: the table id, its URL state and the
 * line above the table. The columns, the row actions and the dialogs live here,
 * so a fix to one is a fix to both.
 */
export function WebhookKeysPanel({
  applicationId,
  header,
  tableId,
  defaultHidden,
  fillHeight,
  search,
  onSearchChange,
  sorting,
  onSortingChange,
  columnFilters,
  onColumnFiltersChange,
}: WebhookKeysPanelProps) {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [createOpen, setCreateOpen] = React.useState(false)
  const [revealValue, setRevealValue] = React.useState<string>()
  const [revokeKey, setRevokeKey] = React.useState<WebhookKeyDto | undefined>()
  const [revokeReason, setRevokeReason] = React.useState("")
  const [rotateKey, setRotateKey] = React.useState<WebhookKeyDto | undefined>()
  const [grace, setGrace] = React.useState("60")
  const gracePresets = useGracePeriodPresets()

  const canCreate = hasPermission(PERMISSIONS.webhookKeys.create)
  const canRevoke = hasPermission(PERMISSIONS.webhookKeys.revoke)
  const canRotate = hasPermission(PERMISSIONS.webhookKeys.rotate)

  // One key shape on every surface, so a change made on either refreshes both.
  const query = useQuery({
    queryKey: ["webhook-keys", { applicationId }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/WebhookKeys", {
          params: { query: applicationId ? { applicationId } : {} },
        })
      ),
    select: revokeReasonOnlyWhenGiven,
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

  const columns = useWebhookKeyColumns({
    defaultHidden,
    onRotate: canRotate ? setRotateKey : undefined,
    onRevoke: canRevoke ? setRevokeKey : undefined,
  })

  const createButton = canCreate ? (
    <Button onClick={() => setCreateOpen(true)} disabled={!applicationId}>
      <Plus data-icon="inline-start" />
      {t("webhookKeys.newKey")}
    </Button>
  ) : null

  return (
    <>
      {header(createButton)}

      <DataTable
        fillHeight={fillHeight}
        tableId={tableId}
        globalSearch
        globalFilter={search}
        onGlobalFilterChange={onSearchChange}
        sorting={sorting}
        onSortingChange={onSortingChange}
        columns={columns}
        data={query.data ?? []}
        // A key that slipped past its date while the alert was up is the most
        // urgent row here, so the link that was meant to surface it must not
        // filter it out: "expiring soon" opens on soon AND already expired.
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
      />

      {applicationId ? (
        <WebhookKeyCreateDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          applicationId={applicationId}
          onCreated={setRevealValue}
        />
      ) : null}

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
          <FieldLabel htmlFor={`${tableId}-revoke-reason`}>
            {t("apiKeys.revokeReason")}
          </FieldLabel>
          <Input
            id={`${tableId}-revoke-reason`}
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
          <FieldLabel htmlFor={`${tableId}-grace`}>
            {t("webhookKeys.gracePeriod")}
          </FieldLabel>
          <PresetField presets={gracePresets} value={grace} onChange={setGrace}>
            {({ value, onChange }) => (
              <Input
                id={`${tableId}-grace`}
                type="number"
                min={MIN_GRACE_PERIOD_MINUTES}
                value={value}
                onChange={(event) => onChange(event.target.value)}
              />
            )}
          </PresetField>
          <FieldDescription>
            {t("webhookKeys.gracePeriodHint")}
          </FieldDescription>
          <FieldConstraints
            min={MIN_GRACE_PERIOD_MINUTES}
            defaultValue={DEFAULT_GRACE_PERIOD_MINUTES}
          />
        </Field>
      </ConfirmDialog>
    </>
  )
}
