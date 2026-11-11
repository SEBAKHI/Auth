import { useQueryClient } from "@tanstack/react-query"
import { KeyRound, Pencil, Plus, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { toNumber } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { usePasskeyAddition } from "@authsystem/auth/passkey/add-passkey"
import { passkeyFailureMessage, withRetryAfter } from "@authsystem/auth/passkey/passkey-errors"
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
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
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

/**
 * Profile › Security › Passkeys (S20 PR C, PK15). Shown only while passkeys run
 * here; until the first answer, and when it is a 404, the page is as it was.
 *
 * - Add in two clicks, through the one addition every page shares
 *   (`usePasskeyAddition`, OI-280 PR B): the first asks for the options and loads
 *   the helper, opening a dialog with a name and, for a first qualifying factor
 *   while email is on, the emailed code (X02); its confirm calls
 *   `startRegistration` before awaiting anything (T-C2). A demand for a fresh proof
 *   opens the window that takes it where the person stands, the passkey alone for
 *   an account whose factor is one (T-C7), and the addition goes on from there.
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

  const [message, setMessage] = React.useState<string | null>(null)
  // The removal that asked for a fresh proof, which goes on once it is given. The addition's
  // window is the addition's own.
  const [reauthenticate, setReauthenticate] = React.useState<{ passkey: Passkey } | null>(null)
  const [removing, setRemoving] = React.useState<Passkey | null>(null)
  const [renamingId, setRenamingId] = React.useState<string | null>(null)

  // The account's only qualifying factor is a passkey, and passkeys run: adding
  // needs a two-factor session, which comes from the passkey alone (PK7, T-C7).
  // Removing does not: a recent password or code will do, or a person who lost
  // that passkey could never remove it.
  const passkeyOnly = me.qualifyingMfa === true && me.totpEnabled !== true

  // N9 (D-150-3 (c)): the first qualifying factor needs the emailed proof, and
  // email is off. The server refuses it (Passkey.EmailRequired); the card says
  // what to do instead of offering a button that fails.
  const needsAppFirst = me.emailCodeAvailable === false && me.qualifyingMfa !== true
  // Email went off since the page read the account: the line below says it, once.
  const shownMessage = needsAppFirst && message === t("profile.passkeys.needsAppFirst") ? null : message
  const refetch = React.useCallback(
    () => queryClient.invalidateQueries({ queryKey: PASSKEYS_QUERY_KEY }),
    [queryClient]
  )

  // What a removal came to, from its confirmation or from the proof it asked for: the card says
  // it, and asks for the proof when the sign-in is too old.
  const settleRemoval = (passkey: Passkey, outcome: RemovalOutcome) => {
    if (outcome.reauthenticate) setReauthenticate({ passkey })
    setMessage(outcome.message ?? null)
    if (outcome.removed) {
      toast.success(t("profile.passkeys.removed", { name: passkey.name }))
    }
    if (outcome.removed || outcome.stale) {
      void refetch()
      void queryClient.invalidateQueries({ queryKey: ["me"], exact: true })
    }
    // After the dialog's own focus return, to the one control that stays.
    window.setTimeout(() => addButtonRef.current?.focus(), 0)
  }

  const added = async (name: string) => {
    toast.success(t("profile.passkeys.added", { name }))
    await refetch()
    void queryClient.invalidateQueries({ queryKey: ["me"], exact: true })
    addButtonRef.current?.focus()
  }

  const addition = usePasskeyAddition({
    passkeyOnly,
    onMessage: setMessage,
    onAdded: (name) => void added(name),
    onClosed: () => addButtonRef.current?.focus(),
  })

  if (passkeys.isPending || list === null) return null
  // A first read that failed says nothing of whether passkeys run here (the
  // gateway answers before the API's 404): the page stays as it was, unless the
  // account is known to hold one. A deployment without passkeys shows no card.
  if (passkeys.isError && list === undefined && toNumber(me.passkeyCount ?? 0) === 0) return null

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

        {shownMessage ? (
          <Alert variant="destructive" data-testid="passkeys-message">
            <AlertDescription>{shownMessage}</AlertDescription>
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
            disabled={addition.starting || passkeys.isError}
            onClick={() => void addition.begin()}
          >
            {addition.starting ? <Spinner data-icon="inline-start" /> : <Plus data-icon="inline-start" />}
            {t("profile.passkeys.add")}
          </Button>
        )}
      </CardContent>

      {addition.dialogs}

      {removing ? (
        <RemovePasskeyDialog
          key={`remove-passkey-${removing.id}`}
          passkey={removing}
          onSettled={(outcome) => {
            setRemoving(null)
            settleRemoval(removing, outcome)
          }}
        />
      ) : null}

      {reauthenticate ? (
        <ReauthenticateDialog
          open
          onOpenChange={(open) => !open && setReauthenticate(null)}
          description={t("profile.passkeys.needsRecentSignIn")}
          // Removing needs no two factors: a person who lost their passkey must still remove it.
          passkey={false}
          onProved={() => {
            // Removing goes on by itself, with the passkey the person confirmed (D-152-5,
            // REVIEW-FIXES 152 F26): a list gone stale meanwhile answers as the confirmation does.
            const confirmed = reauthenticate.passkey
            void requestRemoval(confirmed).then((outcome) => settleRemoval(confirmed, outcome))
          }}
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
  // The rename form closes on its save and on Escape: the focus goes back to the
  // button that opened it, not to the top of the page.
  const renameButtonRef = React.useRef<HTMLButtonElement>(null)
  const wasRenamingRef = React.useRef(false)
  React.useEffect(() => {
    if (wasRenamingRef.current && !renaming) renameButtonRef.current?.focus()
    wasRenamingRef.current = renaming
  }, [renaming])

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
              // No clamp: the sentence's end is its way out, and a phone's row is narrow.
              <ItemDescription className="line-clamp-none text-destructive" data-testid="passkey-counter-regressed">
                {t("profile.passkeys.counterRegressed")}
              </ItemDescription>
            ) : null}
          </>
        )}
      </ItemContent>
      {renaming ? null : (
        <ItemActions>
          <Button
            ref={renameButtonRef}
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

/** What a removal left for the card to say and do. */
interface RemovalOutcome {
  removed?: boolean
  /** The list is out of date (the passkey was already gone). */
  stale?: boolean
  reauthenticate?: boolean
  message?: string | null
}

/** Removes the passkey, and says what that came to: from the confirmation, or after a proof. */
async function requestRemoval(passkey: Passkey): Promise<RemovalOutcome> {
  try {
    const { error, response } = await api.DELETE("/api/v1/auth/passkeys/{id}", {
      params: { path: { id: passkey.id } },
    })
    if (error) throw withRetryAfter(error, response)
    return { removed: true }
  } catch (error) {
    const codes = getErrorCodes(error)
    if (codes.includes("Auth.ReauthenticationRequired")) return { reauthenticate: true }
    if (codes.includes("Passkey.NotFound")) return { stale: true }
    return { message: passkeyFailureMessage(error) }
  }
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
    const outcome = await requestRemoval(passkey)
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
