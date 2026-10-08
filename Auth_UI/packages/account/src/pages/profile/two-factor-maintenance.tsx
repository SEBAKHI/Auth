import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { STALE_TWO_FACTOR_CODES } from "@authsystem/auth/two-factor-codes"
import { AuthenticatorKeyPanel } from "@authsystem/auth/two-factor-enrollment"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
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
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"
import { cn } from "@authsystem/ui/utils"

import { LOW_RECOVERY_CODES } from "./two-factor-status"

/**
 * The warning above the factor's actions: after a sign-in that spent a recovery
 * code (once), or while few codes are left. Nothing otherwise.
 */
export function RecoveryCodesNotice({
  remaining,
  signedInWithRecoveryCode,
  onRegenerate,
}: {
  remaining: number | null
  signedInWithRecoveryCode: boolean
  onRegenerate: () => void
}) {
  const { t } = useTranslation()

  const low = remaining !== null && remaining <= LOW_RECOVERY_CODES
  if (!signedInWithRecoveryCode && !low) return null

  return (
    <Alert>
      <AlertTitle>
        {signedInWithRecoveryCode
          ? t("profile.recoveryCodeUsedTitle")
          : t("profile.recoveryCodesLowTitle")}
      </AlertTitle>
      <AlertDescription>
        <p>
          {signedInWithRecoveryCode
            ? t("profile.recoveryCodeUsed")
            : t("profile.recoveryCodesLow", { count: remaining ?? 0 })}
        </p>
        {/* secondary, not outline: outline sets no text colour and would inherit
            the description's muted one, reading as disabled. */}
        <Button variant="secondary" size="sm" onClick={onRegenerate}>
          {t("profile.regenerateRecoveryCodes")}
        </Button>
      </AlertDescription>
    </Alert>
  )
}

/**
 * A code that proves the factor once more: from the authenticator app, or one of
 * the recovery codes when the phone is gone. Shaped like the disable form's
 * field: the same label, input mode and left-to-right recovery code.
 */
export function SecondFactorCodeField({
  id,
  code,
  onCodeChange,
  useRecoveryCode,
  onUseRecoveryCodeChange,
  description,
  disabled,
}: {
  id: string
  code: string
  onCodeChange: (code: string) => void
  useRecoveryCode: boolean
  onUseRecoveryCodeChange: (useRecoveryCode: boolean) => void
  description?: string
  disabled?: boolean
}) {
  const { t } = useTranslation()

  return (
    <>
      <Field data-disabled={disabled || undefined}>
        <FieldLabel htmlFor={id}>
          {useRecoveryCode ? t("auth.recoveryCode") : t("auth.twoFactorCode")}
        </FieldLabel>
        {/* A recovery code is neither numeric nor a one-time code the browser
            should offer to fill; it is transcribed exactly, so it is pinned LTR
            like the sign-in page's recovery field. */}
        <Input
          id={id}
          value={code}
          onChange={(e) => onCodeChange(e.target.value)}
          inputMode={useRecoveryCode ? undefined : "numeric"}
          autoComplete={useRecoveryCode ? "off" : "one-time-code"}
          dir={useRecoveryCode ? "ltr" : undefined}
          className={cn(useRecoveryCode && "font-mono")}
          disabled={disabled}
        />
        {description ? <FieldDescription>{description}</FieldDescription> : null}
      </Field>
      <Button
        type="button"
        variant="link"
        className="w-fit text-muted-foreground"
        onClick={() => {
          onUseRecoveryCodeChange(!useRecoveryCode)
          onCodeChange("")
        }}
      >
        {useRecoveryCode
          ? t("auth.useAuthenticatorCode")
          : t("auth.useRecoveryCode")}
      </Button>
    </>
  )
}

/** What a change to the factor answers, besides success. */
interface TwoFactorChangeHandlers {
  /** The session is not a recent two-factor one: sign in again. */
  onReauthenticate: () => void
  /** The account's picture of two-factor is out of date: refetch it. */
  onStale: () => void
}

/**
 * One answer to a failure of a change to the factor, keyed by the published code
 * — never by the status: TwoFactor.LockedOut is a 403 too, and only a session
 * that is not a recent two-factor one is fixed by signing in again.
 * @returns whether the dialog should close.
 */
function answerFailure(error: unknown, handlers: TwoFactorChangeHandlers): boolean {
  const codes = getErrorCodes(error)
  if (codes.includes("Auth.ReauthenticationRequired")) {
    handlers.onReauthenticate()
    return true
  }

  toast.error(getErrorMessage(error))

  if (STALE_TWO_FACTOR_CODES.some((stale) => codes.includes(stale))) {
    handlers.onStale()
    return true
  }

  return false
}

/**
 * New recovery codes: one more proof of the factor, from a recent two-factor
 * session; the old codes stop working. Mounted only while open, so closing it
 * resets it.
 */
export function RegenerateRecoveryCodesDialog({
  onOpenChange,
  onRegenerated,
  ...handlers
}: TwoFactorChangeHandlers & {
  onOpenChange: (open: boolean) => void
  /** The new codes, to show once. */
  onRegenerated: (recoveryCodes: string[]) => void
}) {
  const { t } = useTranslation()
  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  // A correct code is spent by the first request: a second click before the
  // button re-renders disabled would be refused as reused.
  const submitting = React.useRef(false)

  const mutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/auth/2fa/recovery-codes", {
          body: { code, useRecoveryCode },
        })
      ),
    onSuccess: (data) => {
      toast.success(t("profile.regenerateRecoveryCodesToast"))
      onRegenerated(data?.recoveryCodes ?? [])
    },
    onError: (error) => {
      if (answerFailure(error, handlers)) onOpenChange(false)
      else setCode("")
    },
  })

  const submit = () => {
    if (!code || submitting.current) return
    submitting.current = true
    mutation.mutate(undefined, {
      onSettled: () => {
        submitting.current = false
      },
    })
  }

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("profile.regenerateRecoveryCodesTitle")}</DialogTitle>
          <DialogDescription>
            {t("profile.regenerateRecoveryCodesBody")}
          </DialogDescription>
        </DialogHeader>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            submit()
          }}
        >
          <FieldGroup>
            <SecondFactorCodeField
              id="regenerate-code"
              code={code}
              onCodeChange={setCode}
              useRecoveryCode={useRecoveryCode}
              onUseRecoveryCodeChange={setUseRecoveryCode}
              description={t("profile.twoFactorChangeNeedsRecentSignIn")}
              disabled={mutation.isPending}
            />
            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
                disabled={mutation.isPending}
              >
                {t("common.cancel")}
              </Button>
              <Button type="submit" disabled={!code || mutation.isPending}>
                {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
                {t("profile.regenerateRecoveryCodes")}
              </Button>
            </DialogFooter>
          </FieldGroup>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Moving the factor to a new authenticator app, in two steps: prove the current
 * factor (the current app, or a recovery code when the phone is gone), which
 * returns the new secret; then scan it and confirm with a code from the new app,
 * which returns a new set of recovery codes. The current app keeps working until
 * the confirmation. Mounted only while open, so closing it resets it.
 */
export function ReplaceAuthenticatorDialog({
  onOpenChange,
  onReplaced,
  ...handlers
}: TwoFactorChangeHandlers & {
  onOpenChange: (open: boolean) => void
  /** The new recovery codes, to show once. */
  onReplaced: (recoveryCodes: string[]) => void
}) {
  const { t } = useTranslation()
  const [secret, setSecret] = React.useState<Schemas["TwoFactorSetupResponse"]>()
  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [newAppCode, setNewAppCode] = React.useState("")
  // Both requests spend what they prove: set synchronously in the handler.
  const submitting = React.useRef(false)

  const begin = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/auth/2fa/replace", {
          body: { code, useRecoveryCode },
        })
      ),
    onSuccess: (data) => {
      setSecret(data)
      setCode("")
      setNewAppCode("")
    },
    onError: (error) => {
      if (answerFailure(error, handlers)) onOpenChange(false)
      else setCode("")
    },
  })

  const confirm = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/auth/2fa/replace/confirm", {
          body: { code: newAppCode },
        })
      ),
    onSuccess: (data) => {
      toast.success(t("profile.replaceAuthenticatorToast"))
      onReplaced(data?.recoveryCodes ?? [])
    },
    onError: (error) => {
      // The new secret expired, or another tab confirmed or replaced it: prove
      // the current factor again for a fresh one.
      if (getErrorCodes(error).includes("TwoFactor.NoPendingReplacement")) {
        toast.error(getErrorMessage(error))
        setSecret(undefined)
        setNewAppCode("")
        return
      }
      if (answerFailure(error, handlers)) onOpenChange(false)
      else setNewAppCode("")
    },
  })

  const run = (mutation: typeof begin | typeof confirm, ready: boolean) => {
    if (!ready || submitting.current) return
    submitting.current = true
    mutation.mutate(undefined, {
      onSettled: () => {
        submitting.current = false
      },
    })
  }

  const pending = begin.isPending || confirm.isPending

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("profile.replaceAuthenticatorTitle")}</DialogTitle>
          <DialogDescription>
            {secret
              ? t("profile.replaceAuthenticatorScanBody")
              : t("profile.replaceAuthenticatorProveBody")}
          </DialogDescription>
        </DialogHeader>
        {secret ? (
          <form
            onSubmit={(event) => {
              event.preventDefault()
              run(confirm, Boolean(newAppCode))
            }}
          >
            <FieldGroup>
              <div className="flex flex-col gap-3">
                <AuthenticatorKeyPanel
                  secret={secret}
                  description={t("profile.setupTwoFactorBody")}
                />
              </div>
              <Field data-disabled={pending || undefined}>
                <FieldLabel htmlFor="replace-new-code">
                  {t("auth.twoFactorCode")}
                </FieldLabel>
                <Input
                  id="replace-new-code"
                  value={newAppCode}
                  onChange={(e) => setNewAppCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  disabled={pending}
                />
              </Field>
              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => onOpenChange(false)}
                  disabled={pending}
                >
                  {t("common.cancel")}
                </Button>
                <Button type="submit" disabled={!newAppCode || pending}>
                  {confirm.isPending ? <Spinner data-icon="inline-start" /> : null}
                  {t("profile.replaceAuthenticatorConfirm")}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        ) : (
          <form
            onSubmit={(event) => {
              event.preventDefault()
              run(begin, Boolean(code))
            }}
          >
            <FieldGroup>
              <SecondFactorCodeField
                id="replace-current-code"
                code={code}
                onCodeChange={setCode}
                useRecoveryCode={useRecoveryCode}
                onUseRecoveryCodeChange={setUseRecoveryCode}
                description={t("profile.twoFactorChangeNeedsRecentSignIn")}
                disabled={pending}
              />
              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => onOpenChange(false)}
                  disabled={pending}
                >
                  {t("common.cancel")}
                </Button>
                <Button type="submit" disabled={!code || pending}>
                  {begin.isPending ? <Spinner data-icon="inline-start" /> : null}
                  {t("common.next")}
                </Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
