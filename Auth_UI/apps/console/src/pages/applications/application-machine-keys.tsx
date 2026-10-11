import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { Download, Plus, TriangleAlert } from "lucide-react"
import * as React from "react"
import { useForm, useWatch } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import { FormDialog } from "@authsystem/ui/common/form-dialog"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@authsystem/ui/input-group"
import { Textarea } from "@authsystem/ui/textarea"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { useAuth } from "@authsystem/auth/auth-context"
import { formatDateTime } from "@authsystem/ui/format"
import { PERMISSIONS } from "@/lib/constants"

type MachineKeyDto = Schemas["MachineKeyDto"]
type CreatedMachineKey = Schemas["CreateMachineKeyResponse"]
type KeySource = "generate" | "upload"

const machineKeysQueryKey = (appId: string) =>
  ["applications", appId, "machine-keys"] as const

/**
 * An application's machine keys (OI-128): the key pairs its own server signs
 * with at the token endpoint. The private key of a generated pair exists in this
 * page for exactly as long as the result dialog is open: it lives in that
 * dialog's state only, and closing the dialog drops it. Nothing writes it to
 * storage, a query cache or a URL.
 */
export function ApplicationMachineKeysTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const canCreate = hasPermission(PERMISSIONS.apiKeys.create)
  const canRevoke = hasPermission(PERMISSIONS.apiKeys.revoke)

  const [createOpen, setCreateOpen] = React.useState(false)
  // A new form on every opening: the dialog is re-mounted, never reset.
  const [createSeq, setCreateSeq] = React.useState(0)
  const [created, setCreated] = React.useState<CreatedMachineKey>()
  const [revokeKey, setRevokeKey] = React.useState<MachineKeyDto>()

  const query = useQuery({
    queryKey: machineKeysQueryKey(appId),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/applications/{applicationId}/machine-keys", {
          params: { path: { applicationId: appId } },
        })
      ),
  })

  const revokeMutation = useMutation({
    mutationFn: async (keyId: string) => {
      const { error } = await api.POST(
        "/api/v1/applications/{applicationId}/machine-keys/{keyId}/revoke",
        { params: { path: { applicationId: appId, keyId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: machineKeysQueryKey(appId),
      })
      toast.success(t("machineKeys.revoked"))
      setRevokeKey(undefined)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns: ColumnDef<MachineKeyDto, unknown>[] = [
    {
      id: "kid",
      accessorFn: (row) => row.kid ?? "",
      header: t("machineKeys.kid"),
      meta: { label: t("machineKeys.kid"), covers: ["name"] },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">
            {row.original.name ?? t("machineKeys.unnamed")}
          </p>
          <p className="font-mono text-xs break-all text-muted-foreground">
            {row.original.kid}
          </p>
        </div>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => (row.isRevoked ? "revoked" : "active"),
      filterFn: "faceted",
      header: t("common.status"),
      meta: {
        label: t("common.status"),
        covers: ["isRevoked", "revokedAt", "revokeReason"],
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
    ...(canRevoke
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => (
              <span className="sr-only">{t("common.actions")}</span>
            ),
            cell: ({ row }) =>
              // A revoked key's status column already says why it has no action.
              row.original.isRevoked ? null : (
                <div className="text-end">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setRevokeKey(row.original)}
                  >
                    {t("machineKeys.revoke")}
                  </Button>
                </div>
              ),
          } satisfies ColumnDef<MachineKeyDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">
        {t("machineKeys.subtitle")}
      </p>
      {canCreate ? (
        <div className="flex justify-end">
          <Button
            onClick={() => {
              setCreateSeq((seq) => seq + 1)
              setCreateOpen(true)
            }}
          >
            <Plus data-icon="inline-start" />
            {t("machineKeys.newKey")}
          </Button>
        </div>
      ) : null}

      <DataTable
        tableId="app-machine-keys"
        columns={columns}
        data={query.data ?? []}
        isLoading={query.isLoading}
        error={query.isError ? query.error : undefined}
        onRetry={() => query.refetch()}
        enableRowDetail={false}
      />

      {canCreate ? (
        <MachineKeyCreateDialog
          key={`machine-key-create-${createSeq}`}
          open={createOpen}
          onOpenChange={setCreateOpen}
          appId={appId}
          onCreated={setCreated}
        />
      ) : null}

      {created ? (
        <MachineKeyCreatedDialog
          created={created}
          // Closing drops the response, private key included: nothing can show it again.
          onClose={() => setCreated(undefined)}
        />
      ) : null}

      <ConfirmDialog
        open={Boolean(revokeKey)}
        onOpenChange={(open) => !open && setRevokeKey(undefined)}
        title={t("machineKeys.revokeTitle")}
        description={t("machineKeys.revokeBody", {
          name: revokeKey?.name ?? revokeKey?.kid ?? "",
        })}
        confirmLabel={t("machineKeys.revoke")}
        destructive
        loading={revokeMutation.isPending}
        onConfirm={() => revokeKey?.id && revokeMutation.mutate(revokeKey.id)}
      />
    </div>
  )
}

function MachineKeyCreateDialog({
  open,
  onOpenChange,
  appId,
  onCreated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  appId: string
  onCreated: (created: CreatedMachineKey) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const schema = z
    .object({
      name: z.string(),
      source: z.enum(["generate", "upload"]),
      publicKeyJwk: z.string(),
    })
    .superRefine((values, ctx) => {
      if (values.source === "upload" && values.publicKeyJwk.trim() === "") {
        ctx.addIssue({
          code: "custom",
          path: ["publicKeyJwk"],
          message: t("validation.required"),
        })
      }
    })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", source: "generate", publicKeyJwk: "" },
  })
  const source = useWatch({ control: form.control, name: "source" })

  const mutation = useMutation({
    mutationFn: (values: Values) =>
      unwrap(
        api.POST("/api/v1/applications/{applicationId}/machine-keys", {
          params: { path: { applicationId: appId } },
          body: {
            name: values.name.trim() === "" ? null : values.name.trim(),
            publicKeyJwk:
              values.source === "upload" ? values.publicKeyJwk.trim() : null,
          },
        })
      ),
    onSuccess: (data) => {
      void queryClient.invalidateQueries({
        queryKey: machineKeysQueryKey(appId),
      })
      toast.success(t("machineKeys.created"))
      onOpenChange(false)
      onCreated(data)
    },
    onError: (error) => {
      // The one refusal that is about a field: show it on the pasted key.
      if (getErrorCodes(error).includes("MachineKey.InvalidPublicKey")) {
        form.setError("publicKeyJwk", { message: getErrorMessage(error) })
        return
      }
      toast.error(getErrorMessage(error))
    },
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={t("machineKeys.createTitle")}
      description={t("machineKeys.createDescription")}
      formId="machine-key-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={t("common.create")}
      pending={mutation.isPending}
    >
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.name")}</FormLabel>
            <FormControl>
              <Input
                placeholder={t("machineKeys.namePlaceholder")}
                {...field}
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="source"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("machineKeys.source")}</FormLabel>
            <FormControl>
              <ToggleGroup
                type="single"
                spacing={0}
                variant="outline"
                value={field.value}
                onValueChange={(next) => {
                  // An empty value is the group deselecting itself on a second click.
                  if (next) field.onChange(next as KeySource)
                }}
              >
                <ToggleGroupItem value="generate">
                  {t("machineKeys.sourceGenerate")}
                </ToggleGroupItem>
                <ToggleGroupItem value="upload">
                  {t("machineKeys.sourceUpload")}
                </ToggleGroupItem>
              </ToggleGroup>
            </FormControl>
            <FormDescription>
              {source === "upload"
                ? t("machineKeys.sourceUploadHint")
                : t("machineKeys.sourceGenerateHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      {source === "upload" ? (
        <FormField
          control={form.control}
          name="publicKeyJwk"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("machineKeys.publicKeyJwk")}</FormLabel>
              <FormControl>
                <Textarea
                  rows={5}
                  className="font-mono text-xs"
                  placeholder={t("machineKeys.publicKeyPlaceholder")}
                  {...field}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
      ) : null}
    </FormDialog>
  )
}

/** Saves `text` as a file named `fileName` in the browser's downloads. */
function downloadText(text: string, fileName: string) {
  const url = URL.createObjectURL(
    new Blob([text], { type: "application/x-pem-file" })
  )
  const link = document.createElement("a")
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function CopyableValue({
  id,
  label,
  value,
}: {
  id: string
  label: string
  value: string
}) {
  const { t } = useTranslation()

  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <InputGroup>
        <InputGroupInput
          id={id}
          readOnly
          value={value}
          className="font-mono text-xs"
          onFocus={(event) => event.currentTarget.select()}
        />
        <InputGroupAddon align="inline-end">
          <CopyButton
            value={value}
            label={t("machineKeys.copyValue", { name: label })}
          />
        </InputGroupAddon>
      </InputGroup>
    </Field>
  )
}

/**
 * What the application's server needs: its client id, the key id, the issuer
 * its assertions are addressed to, the token endpoint, and, for a generated
 * pair, the private key, shown this once.
 */
function MachineKeyCreatedDialog({
  created,
  onClose,
}: {
  created: CreatedMachineKey
  onClose: () => void
}) {
  const { t } = useTranslation()
  const privateKeyPem = created.privateKeyPem ?? null
  const kid = created.kid ?? ""

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>{t("machineKeys.createdTitle")}</DialogTitle>
          <DialogDescription className="flex items-start gap-2">
            {privateKeyPem ? (
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
            ) : null}
            <span>
              {privateKeyPem
                ? t("machineKeys.createdBody")
                : t("machineKeys.createdBodyUploaded")}
            </span>
          </DialogDescription>
        </DialogHeader>

        <FieldGroup>
          <CopyableValue
            id="machine-key-client-id"
            label={t("machineKeys.clientId")}
            value={created.clientId ?? ""}
          />
          <CopyableValue
            id="machine-key-kid"
            label={t("machineKeys.kid")}
            value={kid}
          />
          <CopyableValue
            id="machine-key-issuer"
            label={t("machineKeys.issuer")}
            value={created.issuer ?? ""}
          />
          <CopyableValue
            id="machine-key-token-endpoint"
            label={t("machineKeys.tokenEndpoint")}
            value={created.tokenEndpoint ?? ""}
          />
          {privateKeyPem ? (
            <Field>
              <FieldLabel htmlFor="machine-key-private-key">
                {t("machineKeys.privateKey")}
              </FieldLabel>
              {/* Secret material is transcribed character by character, so it
                  is pinned LTR, as in SecretRevealDialog. */}
              <Textarea
                id="machine-key-private-key"
                readOnly
                dir="ltr"
                rows={6}
                value={privateKeyPem}
                className="font-mono text-xs"
                onFocus={(event) => event.currentTarget.select()}
              />
              <FieldDescription>
                {t("machineKeys.privateKeyHint", {
                  algorithm: created.algorithm ?? "",
                })}
              </FieldDescription>
              <div className="flex justify-end gap-2">
                <CopyButton
                  value={privateKeyPem}
                  label={t("machineKeys.copyValue", {
                    name: t("machineKeys.privateKey"),
                  })}
                />
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => downloadText(privateKeyPem, `${kid}.pem`)}
                >
                  <Download data-icon="inline-start" />
                  {t("machineKeys.download")}
                </Button>
              </div>
            </Field>
          ) : null}
        </FieldGroup>

        <DialogFooter>
          <Button onClick={onClose}>{t("common.close")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
