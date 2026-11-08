import { useQueryClient } from "@tanstack/react-query"
import { KeyRound, Pencil, Plus, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { passkeyFailureMessage, withRetryAfter } from "@authsystem/auth/passkey/passkey-errors"
import {
  isAlreadyOnThisDevice,
  isCeremonyCancelled,
  loadWebAuthn,
  type PublicKeyCredentialCreationOptionsJSON,
  type WebAuthnHelper,
} from "@authsystem/auth/passkey/webauthn"
import { ReauthenticateDialog } from "@authsystem/auth/reauthenticate-dialog"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@authsystem/ui/alert-dialog"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { OneTimeCodeField } from "@authsystem/ui/common/one-time-code-field"
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
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import { parseApiInstant } from "@authsystem/ui/api-instant"
import { formatDateTime, formatRelative } from "@authsystem/ui/format"
import { Input } from "@authsystem/ui/input"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemTitle,
} from "@authsystem/ui/item"
import { Spinner } from "@authsystem/ui/spinner"

import { PASSKEYS_QUERY_KEY, type usePasskeys } from "./passkey-list"

type Passkey = Schemas["PasskeyDto"]

/** A registration begun: the options, the helper, and whether the emailed code is needed. */
interface Registration {
  options: PublicKeyCredentialCreationOptionsJSON
  expiresAt: number
  emailCodeRequired: boolean
  helper: WebAuthnHelper
}

/** A registration's options are not used in their last seconds: the click would only fail. */
const OPTIONS_MARGIN_MS = 5_000

/**
 * Profile › Security › Passkeys (S20 PR C, PK15). Shown only while passkeys run
 * here; until the first answer, and when it is a 404, the page is as it was.
 *
 * - Add in two clicks: the first asks for the options and loads the helper,
 *   opening a dialog with a name and, for a first qualifying factor while email
 *   is on, the emailed code (X02); its confirm calls `startRegistration` before
 *   awaiting anything (T-C2). A demand for a fresh sign-in opens the sign-in-again
 *   dialog (T-C7), naming the passkey for an account whose factor is one.
 * - No "add" while email is off and the account has no qualifying factor (N9,
 *   D-150-3 (c)): the first passkey needs the emailed proof, so the card says to
 *   add the authenticator app first. `Passkey.EmailRequired` is the fallback.
 * - Rename in place; remove behind a confirmation whose button is held until the
 *   request settles, which names the passkey, says the account's other devices
 *   are signed out (PK12) and asks to delete it from the device too.
 */
export function PasskeysCard({
  me,
  passkeys,
}: {
  me: Schemas["UserDto"]
  passkeys: ReturnType<typeof usePasskeys>
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const list = passkeys.data
  const addButtonRef = React.useRef<HTMLButtonElement>(null)

  const [registration, setRegistration] = React.useState<Registration | null>(null)
  // One counter, so a dialog reopened for a new registration mounts afresh.
  const [registrationSeq, setRegistrationSeq] = React.useState(0)
  const [starting, setStarting] = React.useState(false)
  const startingRef = React.useRef(false)
  const [message, setMessage] = React.useState<string | null>(null)
  const [reauthenticate, setReauthenticate] = React.useState(false)
  const [removing, setRemoving] = React.useState<Passkey | null>(null)
  const [renamingId, setRenamingId] = React.useState<string | null>(null)

  // The account's only qualifying factor is a passkey, and passkeys run: a fresh
  // two-factor session comes from the passkey alone (T-C7).
  const passkeyOnly = me.qualifyingMfa === true && me.totpEnabled !== true

  // N9 (D-150-3 (c)): the first qualifying factor needs the emailed proof, and
  // email is off. The server refuses it (Passkey.EmailRequired); the card says
  // what to do instead of offering a button that fails.
  const needsAppFirst = me.emailCodeAvailable === false && me.qualifyingMfa !== true

  const refetch = React.useCallback(
    () => queryClient.invalidateQueries({ queryKey: PASSKEYS_QUERY_KEY }),
    [queryClient]
  )

  const begin = async () => {
    if (startingRef.current) return
    startingRef.current = true
    setStarting(true)
    setMessage(null)
    try {
      const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/options")
      if (error || !data) throw withRetryAfter(error ?? new Error("Passkey options failed"), response)
      // In hand before the confirm, which must call it without awaiting (T-C2).
      const helper = await loadWebAuthn()
      setRegistration({
        options: data.options as PublicKeyCredentialCreationOptionsJSON,
        expiresAt: parseApiInstant(data.expiresAt)?.getTime() ?? Date.now(),
        emailCodeRequired: data.emailCodeRequired,
        helper,
      })
      setRegistrationSeq((seq) => seq + 1)
    } catch (error) {
      if (getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        setReauthenticate(true)
      } else {
        setMessage(passkeyFailureMessage(error))
      }
    } finally {
      startingRef.current = false
      setStarting(false)
    }
  }

  const added = async (name: string) => {
    setRegistration(null)
    toast.success(t("profile.passkeys.added", { name }))
    await refetch()
    void queryClient.invalidateQueries({ queryKey: ["me"], exact: true })
    addButtonRef.current?.focus()
  }

  if (passkeys.isPending || list === null) return null

  return (
    <Card data-testid="passkeys-card">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <KeyRound className="size-4" />
          {t("profile.passkeys.title")}
        </CardTitle>
        <CardDescription>{t("profile.passkeys.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {passkeys.isError ? (
          <Alert variant="destructive">
            <AlertDescription>
              <p>{getErrorMessage(passkeys.error)}</p>
              <Button variant="secondary" size="sm" onClick={() => void passkeys.refetch()}>
                {t("common.retry")}
              </Button>
            </AlertDescription>
          </Alert>
        ) : list && list.passkeys.length > 0 ? (
          <ItemGroup>
            {list.passkeys.map((passkey) => (
              <PasskeyRow
                key={passkey.id}
                passkey={passkey}
                renaming={renamingId === passkey.id}
                onRename={() => setRenamingId(passkey.id)}
                onRenamed={() => {
                  setRenamingId(null)
                  void refetch()
                }}
                onCancelRename={() => setRenamingId(null)}
                onRemove={() => setRemoving(passkey)}
              />
            ))}
          </ItemGroup>
        ) : (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <KeyRound />
              </EmptyMedia>
              <EmptyTitle>{t("profile.passkeys.emptyTitle")}</EmptyTitle>
              <EmptyDescription>{t("profile.passkeys.emptyBody")}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        )}

        {message ? (
          <Alert variant="destructive" data-testid="passkeys-message">
            <AlertDescription>{message}</AlertDescription>
          </Alert>
        ) : null}

        {needsAppFirst ? (
          <p className="text-sm text-muted-foreground" data-testid="passkeys-need-app-first">
            {t("profile.passkeys.needsAppFirst")}
          </p>
        ) : (
          <Button
            ref={addButtonRef}
            variant="outline"
            className="w-fit"
            disabled={starting || passkeys.isError}
            onClick={() => void begin()}
          >
            {starting ? <Spinner data-icon="inline-start" /> : <Plus data-icon="inline-start" />}
            {t("profile.passkeys.add")}
          </Button>
        )}
      </CardContent>

      {/* Mounted only while a registration is open, so closing it resets it. */}
      {registration ? (
        <AddPasskeyDialog
          key={`add-passkey-${registrationSeq}`}
          registration={registration}
          onClose={() => {
            setRegistration(null)
            addButtonRef.current?.focus()
          }}
          onAdded={(name) => void added(name)}
          onStartAgain={() => {
            setRegistration(null)
            void begin()
          }}
          onReauthenticate={() => {
            setRegistration(null)
            setReauthenticate(true)
          }}
        />
      ) : null}

      {removing ? (
        <RemovePasskeyDialog
          key={`remove-passkey-${removing.id}`}
          passkey={removing}
          onSettled={(outcome) => {
            setRemoving(null)
            if (outcome.reauthenticate) setReauthenticate(true)
            setMessage(outcome.message ?? null)
            if (outcome.removed) {
              toast.success(t("profile.passkeys.removed", { name: removing.name }))
            }
            if (outcome.removed || outcome.stale) {
              void refetch()
              void queryClient.invalidateQueries({ queryKey: ["me"], exact: true })
            }
            // After the dialog's own focus return, to the one control that stays.
            window.setTimeout(() => addButtonRef.current?.focus(), 0)
          }}
        />
      ) : null}

      {reauthenticate ? (
        <ReauthenticateDialog
          open
          onOpenChange={(open) => !open && setReauthenticate(false)}
          description={t("profile.passkeys.needsRecentSignIn")}
          passkey={passkeyOnly}
        />
      ) : null}
    </Card>
  )
}

function PasskeyRow({
  passkey,
  renaming,
  onRename,
  onRenamed,
  onCancelRename,
  onRemove,
}: {
  passkey: Passkey
  renaming: boolean
  onRename: () => void
  onRenamed: () => void
  onCancelRename: () => void
  onRemove: () => void
}) {
  const { t } = useTranslation()

  return (
    <Item role="listitem" variant="outline" data-testid="passkey-row">
      <ItemMedia variant="icon">
        <KeyRound />
      </ItemMedia>
      <ItemContent>
        {renaming ? (
          <RenamePasskeyForm passkey={passkey} onDone={onRenamed} onCancel={onCancelRename} />
        ) : (
          <>
            <ItemTitle>
              {/* The owner's own text, in any script. */}
              <span dir="auto">{passkey.name}</span>
              {passkey.isBackedUp ? (
                <Badge variant="secondary">{t("profile.passkeys.synced")}</Badge>
              ) : null}
            </ItemTitle>
            <ItemDescription>
              {t("profile.passkeys.addedOn", { date: formatDateTime(passkey.createdAt) })}
              {" · "}
              {passkey.lastUsedAt
                ? t("profile.passkeys.lastUsed", { when: formatRelative(passkey.lastUsedAt) })
                : t("profile.passkeys.neverUsed")}
            </ItemDescription>
            {passkey.lastCounterRegressionAt ? (
              <ItemDescription className="text-destructive" data-testid="passkey-counter-regressed">
                {t("profile.passkeys.counterRegressed")}
              </ItemDescription>
            ) : null}
          </>
        )}
      </ItemContent>
      {renaming ? null : (
        <ItemActions>
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("profile.passkeys.renameNamed", { name: passkey.name })}
            onClick={onRename}
          >
            <Pencil />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("profile.passkeys.removeNamed", { name: passkey.name })}
            onClick={onRemove}
          >
            <Trash2 />
          </Button>
        </ItemActions>
      )}
    </Item>
  )
}

function RenamePasskeyForm({
  passkey,
  onDone,
  onCancel,
}: {
  passkey: Passkey
  onDone: () => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const [name, setName] = React.useState(passkey.name)
  const [error, setError] = React.useState<string | null>(null)
  const [saving, setSaving] = React.useState(false)
  const savingRef = React.useRef(false)
  const inputId = `passkey-name-${passkey.id}`

  const save = async () => {
    if (savingRef.current) return
    savingRef.current = true
    setSaving(true)
    setError(null)
    try {
      const { error: failure, response } = await api.PATCH("/api/v1/auth/passkeys/{id}", {
        params: { path: { id: passkey.id } },
        body: { name },
      })
      if (failure) throw withRetryAfter(failure, response)
      onDone()
    } catch (failure) {
      if (getErrorCodes(failure).includes("Passkey.NotFound")) {
        // Removed meanwhile (another tab or device): the list is read again.
        onDone()
        return
      }
      setError(passkeyFailureMessage(failure))
    } finally {
      savingRef.current = false
      setSaving(false)
    }
  }

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void save()
      }}
    >
      <FieldGroup>
        <Field data-invalid={error ? true : undefined}>
          <FieldLabel htmlFor={inputId}>{t("profile.passkeys.name")}</FieldLabel>
          <Input
            id={inputId}
            value={name}
            maxLength={128}
            autoFocus
            aria-invalid={error ? true : undefined}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") onCancel()
            }}
          />
          {error ? <FieldError>{error}</FieldError> : null}
        </Field>
        <Field orientation="horizontal">
          <Button type="submit" size="sm" disabled={saving}>
            {saving ? <Spinner data-icon="inline-start" /> : null}
            {t("common.save")}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
            {t("common.cancel")}
          </Button>
        </Field>
      </FieldGroup>
    </form>
  )
}

/**
 * The second click of an addition: a name, the emailed code when the server asks
 * for one, and the confirm that starts the ceremony. The options and the helper
 * are in hand, so the confirm calls `startRegistration` before awaiting anything:
 * a browser grants the ceremony to the click alone (T-C2).
 */
function AddPasskeyDialog({
  registration,
  onClose,
  onAdded,
  onStartAgain,
  onReauthenticate,
}: {
  registration: Registration
  onClose: () => void
  onAdded: (name: string) => void
  /** The registration is over (spent, expired, replaced): fresh options. */
  onStartAgain: () => void
  onReauthenticate: () => void
}) {
  const { t } = useTranslation()
  const [name, setName] = React.useState("")
  const [emailCode, setEmailCode] = React.useState("")
  const [sentTo, setSentTo] = React.useState<string | null>(null)
  const [sending, setSending] = React.useState(false)
  const sendingRef = React.useRef(false)
  const [busy, setBusy] = React.useState(false)
  const busyRef = React.useRef(false)
  const [message, setMessage] = React.useState<string | null>(null)
  const [nameError, setNameError] = React.useState<string | null>(null)
  // The registration's challenge was spent or lost: only fresh options can go on.
  const [over, setOver] = React.useState(false)

  const codeNeeded = registration.emailCodeRequired
  const canConfirm = !busy && !over && (!codeNeeded || (sentTo !== null && emailCode.length === 6))

  const sendCode = async () => {
    if (sendingRef.current || busyRef.current) return
    sendingRef.current = true
    setSending(true)
    setMessage(null)
    try {
      const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/email-code")
      if (error || !data) throw withRetryAfter(error ?? new Error("Email code failed"), response)
      setSentTo(data.sentTo ?? "")
      setEmailCode("")
    } catch (error) {
      // N6: no registration is waiting any more (another page started a sign-in
      // in this browser, or it expired). Its sentence would mislead: start again.
      if (getErrorCodes(error).includes("TwoFactor.SetupRequired")) {
        setOver(true)
        setMessage(t("profile.passkeys.startAgain"))
      } else if (getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        onReauthenticate()
      } else {
        setMessage(passkeyFailureMessage(error))
      }
    } finally {
      sendingRef.current = false
      setSending(false)
    }
  }

  const confirm = () => {
    if (!canConfirm || busyRef.current) return
    // Options in their last seconds would only fail at the server: fresh ones.
    if (Date.now() > registration.expiresAt - OPTIONS_MARGIN_MS) {
      onStartAgain()
      return
    }
    busyRef.current = true
    // Before anything is awaited (T-C2).
    const ceremony = registration.helper.startRegistration({ optionsJSON: registration.options })
    setBusy(true)
    setMessage(null)
    setNameError(null)
    void ceremony.then(
      async (credential) => {
        try {
          const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/verify", {
            body: {
              credential,
              name: name.trim() || null,
              emailCode: codeNeeded ? emailCode : null,
            },
          })
          if (error || !data) throw withRetryAfter(error ?? new Error("Passkey registration failed"), response)
          onAdded(data.name)
        } catch (error) {
          const codes = getErrorCodes(error)
          if (codes.includes("Passkey.NameTooLong")) {
            setNameError(getErrorMessage(error))
          } else if (codes.includes("Auth.ReauthenticationRequired")) {
            onReauthenticate()
            return
          } else {
            // A refused ceremony spends the challenge (T-C13 among the causes):
            // the dialog offers to start again with fresh options.
            if (codes.includes("Passkey.VerificationFailed")) setOver(true)
            setMessage(passkeyFailureMessage(error))
          }
        } finally {
          busyRef.current = false
          setBusy(false)
        }
      },
      (error: unknown) => {
        busyRef.current = false
        setBusy(false)
        if (isCeremonyCancelled(error)) return
        setMessage(
          isAlreadyOnThisDevice(error)
            ? t("profile.passkeys.alreadyOnThisDevice")
            : passkeyFailureMessage(error)
        )
      }
    )
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busyRef.current) onClose()
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("profile.passkeys.addTitle")}</DialogTitle>
          <DialogDescription>{t("profile.passkeys.addBody")}</DialogDescription>
        </DialogHeader>
        <form
          id="add-passkey"
          onSubmit={(event) => {
            event.preventDefault()
            confirm()
          }}
        >
          <FieldGroup>
            <Field data-invalid={nameError ? true : undefined}>
              <FieldLabel htmlFor="new-passkey-name">{t("profile.passkeys.name")}</FieldLabel>
              <Input
                id="new-passkey-name"
                value={name}
                maxLength={128}
                placeholder={t("profile.passkeys.namePlaceholder")}
                aria-invalid={nameError ? true : undefined}
                onChange={(event) => setName(event.target.value)}
              />
              {nameError ? (
                <FieldError>{nameError}</FieldError>
              ) : (
                <FieldDescription>{t("profile.passkeys.nameHint")}</FieldDescription>
              )}
            </Field>
            {codeNeeded ? (
              sentTo === null ? (
                <Field>
                  <FieldDescription>{t("profile.passkeys.emailCodeNeeded")}</FieldDescription>
                  <Button
                    type="button"
                    variant="secondary"
                    className="w-fit"
                    disabled={sending || over}
                    onClick={() => void sendCode()}
                  >
                    {sending ? <Spinner data-icon="inline-start" /> : null}
                    {t("auth.emailMeACode")}
                  </Button>
                </Field>
              ) : (
                <OneTimeCodeField
                  id="new-passkey-email-code"
                  label={t("profile.passkeys.emailCode")}
                  value={emailCode}
                  onChange={setEmailCode}
                  disabled={busy || over}
                >
                  <FieldDescription aria-live="polite">
                    {t("auth.emailCodeSentTo", { email: sentTo })}
                  </FieldDescription>
                </OneTimeCodeField>
              )
            ) : null}
            {message ? (
              <Alert variant="destructive" data-testid="add-passkey-message">
                <AlertDescription>{message}</AlertDescription>
              </Alert>
            ) : null}
          </FieldGroup>
        </form>
        <DialogFooter>
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>
            {t("common.cancel")}
          </Button>
          {over ? (
            <Button type="button" onClick={onStartAgain}>
              {t("profile.passkeys.tryAgain")}
            </Button>
          ) : (
            <Button type="submit" form="add-passkey" disabled={!canConfirm}>
              {busy ? <Spinner data-icon="inline-start" /> : <KeyRound data-icon="inline-start" />}
              {t("profile.passkeys.create")}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** What a removal left for the card to say and do. */
interface RemovalOutcome {
  removed?: boolean
  /** The list is out of date (the passkey was already gone). */
  stale?: boolean
  reauthenticate?: boolean
  message?: string | null
}

/**
 * The removal confirmation. Its button is a plain one, held by a ref until the
 * request settles: the dialog's own action button would close it at the click,
 * before the answer. It closes when the request settles, and the card says what
 * happened.
 */
function RemovePasskeyDialog({
  passkey,
  onSettled,
}: {
  passkey: Passkey
  onSettled: (outcome: RemovalOutcome) => void
}) {
  const { t } = useTranslation()
  const [pending, setPending] = React.useState(false)
  const pendingRef = React.useRef(false)

  const remove = async () => {
    if (pendingRef.current) return
    pendingRef.current = true
    setPending(true)
    let outcome: RemovalOutcome
    try {
      const { error, response } = await api.DELETE("/api/v1/auth/passkeys/{id}", {
        params: { path: { id: passkey.id } },
      })
      if (error) throw withRetryAfter(error, response)
      outcome = { removed: true }
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("Auth.ReauthenticationRequired")) outcome = { reauthenticate: true }
      else if (codes.includes("Passkey.NotFound")) outcome = { stale: true }
      else outcome = { message: passkeyFailureMessage(error) }
    }
    pendingRef.current = false
    setPending(false)
    onSettled(outcome)
  }

  return (
    <AlertDialog
      open
      onOpenChange={(open) => {
        if (!open && !pendingRef.current) onSettled({})
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {t("profile.passkeys.removeTitle", { name: passkey.name })}
          </AlertDialogTitle>
          <AlertDialogDescription>{t("profile.passkeys.removeBody")}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{t("common.cancel")}</AlertDialogCancel>
          <Button variant="destructive" disabled={pending} onClick={() => void remove()}>
            {pending ? <Spinner data-icon="inline-start" /> : <Trash2 data-icon="inline-start" />}
            {t("profile.passkeys.remove")}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
