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
import { ApiKeyCreateDialog } from "./api-key-create-dialog"
import { useApiKeyColumns, type ApiKeyColumnId } from "./api-key-columns"
import { revokeReasonOnlyWhenGiven } from "../credential-key-rows"

type ApiKeyDto = Schemas["ApiKeyDto"]

export interface ApiKeysPanelProps {
  /** The application whose keys are listed and created; empty lists every application. */
  applicationId: string
  /**
   * What sits above the table: the page's header and picker, or the tab's line.
   * Receives the create button, or null when the reader may not create.
   */
  header: (createButton: React.ReactNode) => React.ReactNode
  tableId: string
  defaultHidden?: readonly ApiKeyColumnId[]
  fillHeight?: boolean
  search: string
  onSearchChange: (value: string) => void
  sorting: SortingState
  onSortingChange: (value: SortingState) => void
  columnFilters: ColumnFiltersState
  onColumnFiltersChange: (value: ColumnFiltersState) => void
}

/**
 * The API-key table, one definition for every place that lists API keys: the
 * main-menu page across applications, and an application's own tab (OI-264).
 * Each surface keeps only what is its own: the table id, its URL state and the
 * line above the table. The columns, the row actions and the dialogs live here,
 * so a fix to one is a fix to both.
 */
export function ApiKeysPanel({
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
}: ApiKeysPanelProps) {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const [createOpen, setCreateOpen] = React.useState(false)
  // Each opening of the create dialog mounts a fresh form: its key carries this count.
  const [createSeq, setCreateSeq] = React.useState(0)
  const [revealValue, setRevealValue] = React.useState<string>()
  const [revokeKey, setRevokeKey] = React.useState<ApiKeyDto | undefined>()
  const [revokeReason, setRevokeReason] = React.useState("")
  const [rotateKey, setRotateKey] = React.useState<ApiKeyDto | undefined>()
  const [grace, setGrace] = React.useState("60")
  const gracePresets = useGracePeriodPresets()

  const canCreate = hasPermission(PERMISSIONS.apiKeys.create)
  const canRevoke = hasPermission(PERMISSIONS.apiKeys.revoke)
  const canRotate = hasPermission(PERMISSIONS.apiKeys.rotate)

  // One key shape on every surface, so a change made on either refreshes both.
  const query = useQuery({
    queryKey: ["api-keys", { applicationId }],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/ApiKeys", {
          params: { query: applicationId ? { applicationId } : {} },
        })
      ),
    select: revokeReasonOnlyWhenGiven,
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

  const columns = useApiKeyColumns({
    defaultHidden,
    onRotate: canRotate ? setRotateKey : undefined,
    onRevoke: canRevoke ? setRevokeKey : undefined,
  })

  const createButton = canCreate ? (
    <Button
      onClick={() => {
        setCreateSeq((seq) => seq + 1)
        setCreateOpen(true)
      }}
      disabled={!applicationId}
    >
      <Plus data-icon="inline-start" />
      {t("apiKeys.newKey")}
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
        <ApiKeyCreateDialog
          key={`api-key-create-${applicationId}-${createSeq}`}
          open={createOpen}
          onOpenChange={setCreateOpen}
          applicationId={applicationId}
          onCreated={setRevealValue}
        />
      ) : null}

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
          <FieldLabel htmlFor={`${tableId}-grace`}>
            {t("apiKeys.gracePeriod")}
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
          <FieldDescription>{t("apiKeys.gracePeriodHint")}</FieldDescription>
          <FieldConstraints
            min={MIN_GRACE_PERIOD_MINUTES}
            defaultValue={DEFAULT_GRACE_PERIOD_MINUTES}
          />
        </Field>
      </ConfirmDialog>
    </>
  )
}
