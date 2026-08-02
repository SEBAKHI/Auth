import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { KeyRound, Lock, RefreshCw, Upload } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@authsystem/ui/card"
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { Field, FieldGroup, FieldLabel } from "@authsystem/ui/field"
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
import { Textarea } from "@authsystem/ui/textarea"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime, secretStatusMeta } from "@authsystem/ui/format"
import { Spinner } from "@authsystem/ui/spinner"

const SECRET_STATUS_LABEL: Record<string, string> = {
  notConfigured: "Not configured",
  configured: "Configured",
  empty: "Empty",
  unknown: "Unknown",
}

type GenerateKind = "rsa" | "hmac" | "gateway"
type ImportKind = "rsa" | "hmac" | "gateway"

function ImportDialog({
  open,
  onOpenChange,
  onImported,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onImported: (publicKeyPem?: string | null) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [kind, setKind] = React.useState<ImportKind>("rsa")
  const [value, setValue] = React.useState("")

  React.useEffect(() => {
    if (open) {
      setKind("rsa")
      setValue("")
    }
  }, [open])

  const mutation = useMutation({
    mutationFn: async () => {
      const body = { value }
      if (kind === "rsa") {
        return unwrap(api.POST("/api/v1/admin/Secrets/import/rsa", { body }))
      }
      if (kind === "hmac") {
        return unwrap(api.POST("/api/v1/admin/Secrets/import/hmac", { body }))
      }
      return unwrap(
        api.POST("/api/v1/admin/Secrets/import/gateway-token", { body })
      )
    },
    onSuccess: (data) => {
      void queryClient.invalidateQueries({ queryKey: ["secrets", "status"] })
      toast.success(t("secrets.imported"))
      onOpenChange(false)
      onImported(data?.publicKeyPem)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("secrets.importRsa")}</DialogTitle>
          <DialogDescription>{t("secrets.subtitle")}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="import-secret-kind">
              {t("common.type")}
            </FieldLabel>
            <Select
              value={kind}
              onValueChange={(v) => setKind(v as ImportKind)}
            >
              <SelectTrigger id="import-secret-kind" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem value="rsa">RSA</SelectItem>
                  <SelectItem value="hmac">HMAC</SelectItem>
                  <SelectItem value="gateway">Gateway token</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="import-secret-value">
              {t("secrets.value")}
            </FieldLabel>
            {/* Key material — pinned LTR like the key field above it, so an RTL
                console cannot right-align or reorder a PEM blob. */}
            <Textarea
              id="import-secret-value"
              dir="ltr"
              rows={6}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder={t("secrets.importValuePlaceholder")}
              className="font-mono text-xs"
            />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => value && mutation.mutate()}
            disabled={!value || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("common.confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function CustomSecretDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [key, setKey] = React.useState("")
  const [value, setValue] = React.useState("")

  React.useEffect(() => {
    if (open) {
      setKey("")
      setValue("")
    }
  }, [open])

  const mutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT("/api/v1/admin/Secrets/custom/{key}", {
        params: { path: { key } },
        body: { value },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["secrets", "status"] })
      toast.success(t("secrets.customSet"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("secrets.setCustom")}</DialogTitle>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="custom-secret-key">
              {t("secrets.key")}
            </FieldLabel>
            <Input
              id="custom-secret-key"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              placeholder="Smtp:Password"
              dir="ltr"
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="custom-secret-value">
              {t("secrets.value")}
            </FieldLabel>
            <Input
              id="custom-secret-value"
              dir="ltr"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              className="font-mono text-xs"
            />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={() => key && value && mutation.mutate()}
            disabled={!key || !value || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function SecretsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [importOpen, setImportOpen] = React.useState(false)
  const [customOpen, setCustomOpen] = React.useState(false)
  const [deleteOpen, setDeleteOpen] = React.useState(false)
  const [deleteKey, setDeleteKey] = React.useState("")
  const [pendingGenerate, setPendingGenerate] = React.useState<GenerateKind>()
  const [reveal, setReveal] = React.useState<{
    value: string
    multiline: boolean
  }>()

  const statusQuery = useQuery({
    queryKey: ["secrets", "status"],
    retry: false,
    queryFn: () => unwrap(api.GET("/api/v1/admin/Secrets/status")),
  })

  const generateMutation = useMutation({
    mutationFn: async (kind: GenerateKind) => {
      if (kind === "rsa") {
        const data = await unwrap(
          api.POST("/api/v1/admin/Secrets/generate/rsa")
        )
        return { value: data?.publicKeyPem, multiline: true }
      }
      if (kind === "gateway") {
        const data = await unwrap(
          api.POST("/api/v1/admin/Secrets/generate/gateway-token")
        )
        return { value: data?.token, multiline: false }
      }
      await unwrap(api.POST("/api/v1/admin/Secrets/generate/hmac"))
      return { value: undefined, multiline: false }
    },
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ["secrets", "status"] })
      toast.success(t("secrets.generated"))
      setPendingGenerate(undefined)
      if (result.value) {
        setReveal({ value: result.value, multiline: result.multiline })
      }
    },
    onError: (error) => {
      setPendingGenerate(undefined)
      toast.error(getErrorMessage(error))
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (key: string) => {
      const { error } = await api.DELETE("/api/v1/admin/Secrets/custom/{key}", {
        params: { path: { key } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["secrets", "status"] })
      toast.success(t("secrets.customDeleted"))
      setDeleteOpen(false)
      setDeleteKey("")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  if (statusQuery.isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title={t("secrets.title")}
          description={t("secrets.subtitle")}
        />
        <Card>
          <CardContent className="flex flex-col items-center gap-2 py-12 text-center">
            <Lock className="size-8 text-muted-foreground" />
            <p className="font-medium">{t("secrets.disabledTitle")}</p>
            <p className="max-w-md text-sm text-muted-foreground">
              {t("secrets.disabledBody")}
            </p>
          </CardContent>
        </Card>
      </div>
    )
  }

  const status = statusQuery.data
  const secrets = Object.entries(status?.secrets ?? {})

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("secrets.title")}
        description={t("secrets.subtitle")}
        actions={
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => setImportOpen(true)}>
              <Upload data-icon="inline-start" />
              {t("secrets.importRsa")}
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button disabled={generateMutation.isPending}>
                  <KeyRound data-icon="inline-start" />
                  {t("secrets.generate")}
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuGroup>
                  <DropdownMenuItem onClick={() => setPendingGenerate("rsa")}>
                    {t("secrets.generateRsa")}
                  </DropdownMenuItem>
                  <DropdownMenuItem onClick={() => setPendingGenerate("hmac")}>
                    {t("secrets.generateHmac")}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => setPendingGenerate("gateway")}
                  >
                    {t("secrets.generateGatewayToken")}
                  </DropdownMenuItem>
                </DropdownMenuGroup>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        }
      />

      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="text-base">{t("secrets.secretFile")}</CardTitle>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={t("common.refresh")}
            onClick={() => statusQuery.refetch()}
          >
            <RefreshCw />
          </Button>
        </CardHeader>
        <CardContent className="flex flex-col gap-2 text-sm">
          {statusQuery.isLoading ? (
            <Skeleton className="h-20 w-full" />
          ) : (
            <>
              <p className="truncate font-mono text-xs text-muted-foreground">
                {status?.secretFilePath}
              </p>
              <div className="flex flex-wrap gap-x-6 gap-y-1 text-muted-foreground">
                <span>
                  {t("secrets.machine")}: {status?.machineName ?? "—"}
                </span>
                <span>
                  {t("secrets.schemaVersion")}: {status?.schemaVersion ?? "—"}
                </span>
                <span>
                  {t("common.modifiedAt")}:{" "}
                  {formatDateTime(status?.lastModified)}
                </span>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t("secrets.title")}</CardTitle>
        </CardHeader>
        <CardContent>
          {secrets.length === 0 ? (
            <p className="py-4 text-center text-sm text-muted-foreground">
              {t("common.empty")}
            </p>
          ) : (
            <ul className="divide-y">
              {secrets.map(([key, value]) => {
                const meta = secretStatusMeta(value)
                return (
                  <li
                    key={key}
                    className="flex items-center justify-between gap-3 py-2.5"
                  >
                    <span className="font-mono text-sm">{key}</span>
                    <Badge variant={meta.variant}>
                      {SECRET_STATUS_LABEL[meta.key]}
                    </Badge>
                  </li>
                )
              })}
            </ul>
          )}
          <div className="mt-4 flex flex-wrap gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setCustomOpen(true)}
            >
              {t("secrets.setCustom")}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setDeleteKey("")
                setDeleteOpen(true)
              }}
            >
              {t("secrets.deleteCustom")}
            </Button>
          </div>
        </CardContent>
      </Card>

      <ImportDialog
        open={importOpen}
        onOpenChange={setImportOpen}
        onImported={(pem) =>
          pem ? setReveal({ value: pem, multiline: true }) : undefined
        }
      />
      <CustomSecretDialog open={customOpen} onOpenChange={setCustomOpen} />

      <ConfirmDialog
        open={Boolean(pendingGenerate)}
        onOpenChange={(open) => !open && setPendingGenerate(undefined)}
        title={t("secrets.generate")}
        description={t("secrets.rotateWarning")}
        confirmLabel={t("secrets.generate")}
        destructive
        loading={generateMutation.isPending}
        onConfirm={() =>
          pendingGenerate && generateMutation.mutate(pendingGenerate)
        }
      />

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={(open) => {
          if (!open) {
            setDeleteOpen(false)
            setDeleteKey("")
          }
        }}
        title={t("secrets.deleteCustomTitle")}
        description={t("secrets.deleteCustomBody", { key: deleteKey || "…" })}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleteKey && deleteMutation.mutate(deleteKey)}
      >
        <Field>
          <FieldLabel htmlFor="delete-key">{t("secrets.key")}</FieldLabel>
          <Input
            id="delete-key"
            value={deleteKey}
            onChange={(e) => setDeleteKey(e.target.value)}
          />
        </Field>
      </ConfirmDialog>

      <SecretRevealDialog
        open={Boolean(reveal)}
        onOpenChange={(open) => !open && setReveal(undefined)}
        title={t("secrets.publicKey")}
        value={reveal?.value ?? ""}
        multiline={reveal?.multiline ?? false}
      />
    </div>
  )
}
