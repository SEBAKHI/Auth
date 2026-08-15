import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { KeyRound, Lock, Pencil, RefreshCw, TriangleAlert } from "lucide-react"
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
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
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
import { Skeleton } from "@authsystem/ui/skeleton"
import { Textarea } from "@authsystem/ui/textarea"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import {
  getErrorCodes,
  getErrorMessage,
  getErrorStatus,
} from "@authsystem/api/errors"
import { formatDateTime, secretStatusMeta } from "@authsystem/ui/format"
import { Spinner } from "@authsystem/ui/spinner"

import {
  SecretOperationFlow,
  type PendingSecretOperation,
  type SecretOperationName,
} from "./secret-operation-flow"

/**
 * The three shapes key material comes in. Not three secrets and not a free
 * choice: each shape is accepted by exactly one pair of endpoints, which write
 * to a fixed secret name. There is no "which secret" parameter anywhere on the
 * import path, which is why a credential like the connection string can never
 * travel through it.
 */
type ImportKind = "rsa" | "hmac" | "gateway"

interface KeyMaterialSpec {
  generate: SecretOperationName
  import: SecretOperationName
  generateLabelKey: string
  importLabelKey: string
  /** Names the exact encoding the server will validate the pasted value against. */
  valueLabelKey: string
}

const KEY_MATERIAL: Record<ImportKind, KeyMaterialSpec> = {
  rsa: {
    generate: "GenerateRsaKey",
    import: "ImportRsaKey",
    generateLabelKey: "secrets.generateRsa",
    importLabelKey: "secrets.importRsa",
    valueLabelKey: "secrets.rsaPem",
  },
  hmac: {
    generate: "GenerateHmacKey",
    import: "ImportHmacKey",
    generateLabelKey: "secrets.generateHmac",
    importLabelKey: "secrets.importHmac",
    valueLabelKey: "secrets.hmacBase64",
  },
  gateway: {
    generate: "GenerateGatewayToken",
    import: "ImportGatewayToken",
    generateLabelKey: "secrets.generateGatewayToken",
    importLabelKey: "secrets.importGatewayToken",
    valueLabelKey: "secrets.tokenValue",
  },
}

/**
 * Collects the key material only. Importing is destructive, so the dialog hands
 * the material to the confirmation flow rather than posting it: the code the
 * administrator is about to be emailed is bound to a digest of these exact
 * bytes, which is why they are chosen before the confirmation and not after.
 *
 * The shape is fixed by the row this was opened from, so there is nothing to
 * pick here. The dialog used to ask — and naming one shape in the button while
 * offering three in the dialog is what made the page read as if importing were
 * a general-purpose editor for any secret.
 */
function ImportDialog({
  kind,
  onOpenChange,
  onSubmit,
}: {
  kind?: ImportKind
  onOpenChange: (open: boolean) => void
  onSubmit: (pending: PendingSecretOperation) => void
}) {
  const { t } = useTranslation()
  // No reset effect: the caller keys this component by kind, so opening it
  // mounts a fresh instance and the initial state IS the reset. Key material
  // half-pasted for one shape must never survive into another.
  const [value, setValue] = React.useState("")

  const spec = kind ? KEY_MATERIAL[kind] : undefined

  return (
    <Dialog open={Boolean(kind)} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{spec ? t(spec.importLabelKey) : ""}</DialogTitle>
          <DialogDescription>{t("secrets.importBody")}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="import-secret-value">
              {spec ? t(spec.valueLabelKey) : ""}
            </FieldLabel>
            {/* Key material — pinned LTR so an RTL console cannot right-align
                or reorder a PEM blob. */}
            <Textarea
              id="import-secret-value"
              dir="ltr"
              rows={6}
              spellCheck={false}
              autoComplete="off"
              autoCorrect="off"
              autoCapitalize="off"
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
              if (!value || !spec) return
              onOpenChange(false)
              onSubmit({ operation: spec.import, value })
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
 * What governs each first-class secret, and therefore what its row may offer.
 *
 * The split is not cosmetic. It follows from a single question — who owns the
 * correct value? — and everything else is downstream of the answer:
 *
 *  - An external party owns it (the mail provider, the database server), so the
 *    system can only transcribe what they decided. Generating one would be
 *    meaningless, and a wrong value fails forward without killing anything
 *    already issued. Those rows carry a plain edit form: `SETTABLE_KNOWN_SECRETS`.
 *  - The system owns it, so it can be minted — and replacing it does not make
 *    the old value "wrong", it makes it *gone*, along with everything ever
 *    derived from it. Those rows carry `material`, and both generating and
 *    importing run the three-gate confirmation in `SecretOperationFlow`.
 *  - Neither: the value is derived, permanent, or not a single value at all.
 *    Those rows carry a description and no action.
 *
 * Every row therefore states its own governance. Six of the eight used to offer
 * nothing and explain nothing, which reads as an unfinished feature rather than
 * a deliberate refusal — and left the destructive operations parked in the page
 * header, far from the rows they rewrite.
 *
 * Keys match the names the status endpoint reports. Anything absent here (a
 * `Custom:` entry) renders as a plain row, which is correct: nothing reads it.
 */
const SECRET_GOVERNANCE: Record<
  string,
  { descriptionKey: string; material?: ImportKind }
> = {
  JwtPrivateKeyPem: {
    descriptionKey: "secrets.about.jwtPrivateKeyPem",
    material: "rsa",
  },
  JwtPublicKeyPem: { descriptionKey: "secrets.about.jwtPublicKeyPem" },
  RefreshTokenHmacKey: {
    descriptionKey: "secrets.about.refreshTokenHmacKey",
    material: "hmac",
  },
  GatewayToken: {
    descriptionKey: "secrets.about.gatewayToken",
    material: "gateway",
  },
  AccountDeletionIdentifierHmacKey: {
    descriptionKey: "secrets.about.accountDeletionIdentifierHmacKey",
  },
  PasswordPepper: { descriptionKey: "secrets.about.passwordPepper" },
  SmtpPassword: { descriptionKey: "secrets.about.smtpPassword" },
  "ConnectionStrings.AuthDb": {
    descriptionKey: "secrets.about.connectionString",
  },
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
  // No reset effect: the caller keys this component by `open`, so opening it
  // mounts a fresh instance and the initial state IS the reset — the same
  // arrangement the other two dialogs on this page use.
  const [key, setKey] = React.useState("")
  const [value, setValue] = React.useState("")

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
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()

  const [importKind, setImportKind] = React.useState<ImportKind>()
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
    // Language-scoped, because the failure branch renders the server's own
    // words: the error is localized from Accept-Language at the time of the
    // call, and re-rendering under a new language leaves a cached message in
    // the old one — an English page explaining itself in Persian. Every
    // invalidate elsewhere passes ["secrets", "status"], which still matches
    // by prefix.
    queryKey: ["secrets", "status", i18n.language],
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
    // 403 is the only status that means what this screen used to claim for every
    // failure: the admin API is switched off for this environment, or the caller
    // lacks secrets.manage. Anything else is a fault — a secrets file this host
    // can no longer decrypt returns 500 with the reason in its body — and
    // reporting that as "disabled" tells the operator the opposite of the truth
    // while hiding the one thing they could act on.
    const refused = getErrorStatus(statusQuery.error) === 403
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title={t("secrets.title")}
          description={t("secrets.subtitle")}
        />
        <Card>
          <CardContent>
            <Empty>
              <EmptyHeader>
                <EmptyMedia variant="icon">
                  {refused ? <Lock /> : <TriangleAlert />}
                </EmptyMedia>
                <EmptyTitle>
                  {refused
                    ? t("secrets.disabledTitle")
                    : t("secrets.unavailableTitle")}
                </EmptyTitle>
                {/* The server's own words on the fault path. The handler turns
                    both failure modes into domain errors that already name the
                    cause and the fix, so paraphrasing them here would only lose
                    detail. */}
                <EmptyDescription>
                  {refused
                    ? t("secrets.disabledBody")
                    : getErrorMessage(statusQuery.error)}
                </EmptyDescription>
              </EmptyHeader>
              {refused ? null : (
                <EmptyContent>
                  <Button
                    variant="outline"
                    onClick={() => statusQuery.refetch()}
                  >
                    <RefreshCw data-icon="inline-start" />
                    {t("common.retry")}
                  </Button>
                </EmptyContent>
              )}
            </Empty>
          </CardContent>
        </Card>
      </div>
    )
  }

  const status = statusQuery.data
  const secrets = Object.entries(status?.secrets ?? {})

  return (
    <div className="flex flex-col gap-6">
      {/* No page-level actions. Every operation belongs to exactly one secret,
          so it lives on that secret's row — a generate/import pair in the
          header could only name the key it rewrites in its label, and did so
          for one of the three. */}
      <PageHeader
        title={t("secrets.title")}
        description={t("secrets.subtitle")}
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
                const governance = SECRET_GOVERNANCE[key]
                const material = governance?.material
                  ? KEY_MATERIAL[governance.material]
                  : undefined
                return (
                  <li
                    key={key}
                    className="flex items-center justify-between gap-3 py-2.5"
                  >
                    <div className="flex min-w-0 flex-col gap-0.5">
                      <span className="font-mono text-sm">{key}</span>
                      {/* Why this row offers what it offers. Rows that cannot
                          be acted on need this most: a blank row reads as a
                          missing button rather than a deliberate one. */}
                      {governance ? (
                        <span className="text-xs text-muted-foreground">
                          {t(governance.descriptionKey)}
                        </span>
                      ) : null}
                    </div>
                    {/* Action first, badge last, so the badges stay on one
                        column the eye can scan down regardless of which rows
                        carry a button. Source order, not a logical-property
                        override, so RTL mirrors it for free. */}
                    <div className="flex shrink-0 items-center gap-2">
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
                      {material ? (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button
                              variant="ghost"
                              size="sm"
                              disabled={Boolean(pendingOperation)}
                            >
                              <KeyRound data-icon="inline-start" />
                              {t("secrets.replace")}
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuGroup>
                              <DropdownMenuItem
                                onClick={() =>
                                  setPendingOperation({
                                    operation: material.generate,
                                  })
                                }
                              >
                                {t(material.generateLabelKey)}
                              </DropdownMenuItem>
                              <DropdownMenuItem
                                onClick={() =>
                                  setImportKind(governance?.material)
                                }
                              >
                                {t(material.importLabelKey)}
                              </DropdownMenuItem>
                            </DropdownMenuGroup>
                          </DropdownMenuContent>
                        </DropdownMenu>
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

      {/* Keyed so opening a dialog mounts a fresh one and the initial state IS
          the reset; prefixed because these two are siblings and would otherwise
          collide on the closed key. */}
      <ImportDialog
        key={`import-${importKind ?? "none"}`}
        kind={importKind}
        onOpenChange={(open) => {
          if (!open) setImportKind(undefined)
        }}
        onSubmit={setPendingOperation}
      />
      <CustomSecretDialog
        key={`custom-${customOpen}`}
        open={customOpen}
        onOpenChange={setCustomOpen}
      />
      <KnownSecretDialog
        key={`known-${knownSecretKey ?? "none"}`}
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
