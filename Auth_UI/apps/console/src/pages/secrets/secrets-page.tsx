import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { KeyRound, Lock, Pencil, RefreshCw, Upload } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
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
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
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
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime, secretStatusMeta } from "@authsystem/ui/format"
import { Spinner } from "@authsystem/ui/spinner"

import {
  SecretOperationFlow,
  type PendingSecretOperation,
  type SecretOperationName,
} from "./secret-operation-flow"

type ImportKind = "rsa" | "hmac" | "gateway"

const IMPORT_OPERATION: Record<ImportKind, SecretOperationName> = {
  rsa: "ImportRsaKey",
  hmac: "ImportHmacKey",
  gateway: "ImportGatewayToken",
}

/**
 * Collects the key material only. Importing is destructive, so the dialog hands
 * the material to the confirmation flow rather than posting it: the code the
 * administrator is about to be emailed is bound to a digest of these exact
 * bytes, which is why they are chosen before the confirmation and not after.
 */
function ImportDialog({
  open,
  onOpenChange,
  onSubmit,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (pending: PendingSecretOperation) => void
}) {
  const { t } = useTranslation()
  const [kind, setKind] = React.useState<ImportKind>("rsa")
  const [value, setValue] = React.useState("")

  React.useEffect(() => {
    if (open) {
      setKind("rsa")
      setValue("")
    }
  }, [open])

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
            variant="destructive"
            onClick={() => {
              if (!value) return
              onOpenChange(false)
              onSubmit({ operation: IMPORT_OPERATION[kind], value })
            }}
            disabled={!value}
          >
            {t("secrets.challengeContinue")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * The two first-class secrets that carry a credential rather than key material,
 * keyed by the name the status endpoint reports them under.
 *
 * Both used to be supplied as plaintext environment variables in web.config,
 * which is why they render as "not configured": the badge reads the encrypted
 * file, not the resolved configuration. The generic custom-secret dialog cannot
 * set them — it namespaces every key under `Custom:`, which lands the value in
 * `Secrets:Custom:*` where nothing reads it.
 */
const SETTABLE_KNOWN_SECRETS = {
  SmtpPassword: {
    endpoint: "/api/v1/admin/Secrets/smtp-password",
    labelKey: "secrets.setSmtpPassword",
    hintKey: "secrets.setSmtpPasswordHint",
    multiline: false,
  },
  "ConnectionStrings.AuthDb": {
    endpoint: "/api/v1/admin/Secrets/connection-string",
    labelKey: "secrets.setConnectionString",
    hintKey: "secrets.setConnectionStringHint",
    multiline: true,
  },
} as const

type KnownSecretKey = keyof typeof SETTABLE_KNOWN_SECRETS

function isSettableKnownSecret(key: string): key is KnownSecretKey {
  return Object.hasOwn(SETTABLE_KNOWN_SECRETS, key)
}

/**
 * Stores one of the two credential secrets in the encrypted file.
 *
 * The connection string is probed before it is stored. An unreachable server is
 * reported but not fatal: rotating the database password has no other valid
 * order, since changing it at the server first takes this very console down with
 * the database, and storing the new string first cannot pass a connect test
 * while the credential is not live yet. So the first failure surfaces as a
 * warning and arms a second, deliberate "save anyway" click.
 */
function KnownSecretDialog({
  secretKey,
  onOpenChange,
}: {
  secretKey?: KnownSecretKey
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  // No reset effect: the caller keys this component by secretKey, so opening it
  // mounts a fresh instance and the initial state IS the reset. A half-typed
  // connection string must never survive into the next dialog.
  const [value, setValue] = React.useState("")
  // The warning carries the value it was raised for, not just its text. Clearing
  // it on keystroke would only cover the forward direction: the field stays
  // editable during the probe, so a late-arriving failure for the OLD string
  // would otherwise arm "Save anyway" for a string that was never probed, and
  // force-save skips the server-side check. Comparing against `probed` ties the
  // confirmation to exactly the value it describes, whichever order they arrive in.
  const [unreachable, setUnreachable] = React.useState<{
    probed: string
    message: string
  }>()

  const config = secretKey ? SETTABLE_KNOWN_SECRETS[secretKey] : undefined

  // Armed only while the field still holds the string the server rejected.
  const forceSave = unreachable?.probed === value
  const warning = forceSave ? unreachable.message : undefined

  const mutation = useMutation({
    mutationFn: async (attempt: string) => {
      if (!config) return
      const { error } = await api.PUT(config.endpoint, {
        body: config.multiline ? { value: attempt, forceSave } : { value: attempt },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["secrets", "status"] })
      toast.success(t("secrets.knownSecretSaved"))
      onOpenChange(false)
    },
    onError: (error, attempt) => {
      if (getErrorCodes(error).includes("Secret.ConnectionStringUnreachable")) {
        setUnreachable({ probed: attempt, message: getErrorMessage(error) })
        return
      }
      toast.error(getErrorMessage(error))
    },
  })

  return (
    <Dialog open={Boolean(secretKey)} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{config ? t(config.labelKey) : ""}</DialogTitle>
          <DialogDescription>{t("secrets.restartRequired")}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="known-secret-value">
              {t("secrets.value")}
            </FieldLabel>
            {/* Pinned LTR: a connection string or password must never be
                right-aligned or reordered by an RTL console.
                spellCheck/autoComplete are off on both: a connection string in a
                plain textarea is prose to the browser, and enhanced spellcheck
                ships the field contents — password included — to the vendor. */}
            {config?.multiline ? (
              <Textarea
                id="known-secret-value"
                dir="ltr"
                rows={4}
                spellCheck={false}
                autoComplete="off"
                autoCorrect="off"
                autoCapitalize="off"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                className="font-mono text-xs"
              />
            ) : (
              <Input
                id="known-secret-value"
                type="password"
                dir="ltr"
                spellCheck={false}
                autoComplete="new-password"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                className="font-mono text-xs"
              />
            )}
            <FieldDescription>
              {config ? t(config.hintKey) : ""}
            </FieldDescription>
          </Field>
        </FieldGroup>
        {warning ? (
          <Alert variant="destructive">
            <AlertTitle>{t("secrets.connectionFailedTitle")}</AlertTitle>
            <AlertDescription>
              {warning} {t("secrets.connectionFailedBody")}
            </AlertDescription>
          </Alert>
        ) : null}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            variant={forceSave ? "destructive" : "default"}
            onClick={() => value && mutation.mutate(value)}
            disabled={!value || mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {forceSave ? t("secrets.saveAnyway") : t("common.save")}
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
  const [knownSecretKey, setKnownSecretKey] = React.useState<KnownSecretKey>()
  const [deleteOpen, setDeleteOpen] = React.useState(false)
  const [deleteKey, setDeleteKey] = React.useState("")
  const [pendingOperation, setPendingOperation] =
    React.useState<PendingSecretOperation>()
  const [reveal, setReveal] = React.useState<{
    value: string
    multiline: boolean
  }>()

  const statusQuery = useQuery({
    queryKey: ["secrets", "status"],
    retry: false,
    queryFn: () => unwrap(api.GET("/api/v1/admin/Secrets/status")),
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
                <Button disabled={Boolean(pendingOperation)}>
                  <KeyRound data-icon="inline-start" />
                  {t("secrets.generate")}
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuGroup>
                  <DropdownMenuItem
                    onClick={() =>
                      setPendingOperation({ operation: "GenerateRsaKey" })
                    }
                  >
                    {t("secrets.generateRsa")}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() =>
                      setPendingOperation({ operation: "GenerateHmacKey" })
                    }
                  >
                    {t("secrets.generateHmac")}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() =>
                      setPendingOperation({
                        operation: "GenerateGatewayToken",
                      })
                    }
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
                    {/* Action first, badge last. Only two rows carry an action,
                        so putting it after the badge would indent those two
                        badges by the button's width and break the column the
                        eye scans down. Source order, not a logical-property
                        override, so RTL mirrors it for free. */}
                    <div className="flex items-center gap-2">
                      {isSettableKnownSecret(key) ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setKnownSecretKey(key)}
                        >
                          <Pencil data-icon="inline-start" />
                          {t("common.edit")}
                        </Button>
                      ) : null}
                      {/* `secretStatusMeta` returns one of four fixed keys, all
                          under `secrets.status`. */}
                      <Badge variant={meta.variant}>
                        {t(`secrets.status.${meta.key}`)}
                      </Badge>
                    </div>
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
        onSubmit={setPendingOperation}
      />
      <CustomSecretDialog open={customOpen} onOpenChange={setCustomOpen} />
      <KnownSecretDialog
        key={knownSecretKey ?? "none"}
        secretKey={knownSecretKey}
        onOpenChange={(open) => {
          if (!open) setKnownSecretKey(undefined)
        }}
      />

      {pendingOperation ? (
        <SecretOperationFlow
          pending={pendingOperation}
          onClose={() => setPendingOperation(undefined)}
          onExecuted={(result) =>
            result.value
              ? setReveal({ value: result.value, multiline: result.multiline })
              : undefined
          }
        />
      ) : null}

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
