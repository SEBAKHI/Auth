import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Mail } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { ReauthenticateDialog } from "@authsystem/auth/reauthenticate-dialog"
import { STALE_TWO_FACTOR_CODES } from "@authsystem/auth/two-factor-codes"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { Field, FieldDescription, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"
import { cn } from "@authsystem/ui/utils"

/** A resend waits as long as the server's per-address window asks. */
const RESEND_COOLDOWN_MS = 60_000

/**
 * Where a code went: the masked address, when the next one may be asked for, and
 * how many more this enrollment or switch-off may send — none left, no resend is
 * offered, since it could only be refused (OI-220 (1)).
 */
interface CodeSent {
  maskedEmail: string
  resendAt: Date
  sendsRemaining: number
}

type Step = "replace-warning" | "confirm" | "disable"

/**
 * The emailed code as the account's second factor (S24): after the password,
 * sign-in asks for a six-digit code sent to the account's address. Opt-in, and
 * weaker than an authenticator app — whoever holds the mailbox holds the password
 * reset too — so no two-factor policy counts it.
 *
 * Switching it on emails a code that proves the mailbox; over an authenticator app
 * it replaces the app (the two are never bound together), which the person is
 * warned of first. Switching it off asks for a code emailed for that, or a
 * recovery code, and signs the other sessions out. A card that cannot act says why:
 * an account without a password (a provider sign-in proves the same mailbox), or
 * email sending switched off.
 */
export function EmailCodeCard({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const enabled = me.emailCodeEnabled === true
  // Offered only when the server says it can be (review of #107): an API that does
  // not say is one without the feature, whose routes answer 404.
  const known = me.emailCodeAvailable != null
  const available = me.emailCodeAvailable === true
  const hasPassword = me.hasPassword !== false
  const totpEnabled = me.totpEnabled === true
  // A policy keeps the app (S08): the emailed code in its place would be refused.
  const keptByPolicy = totpEnabled && me.twoFactorRequiredByPolicy === true

  const [step, setStep] = React.useState<Step>()
  const [sent, setSent] = React.useState<CodeSent>()
  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)
  // A send replaces the code before it, and a confirm spends it: a second click
  // landing before the buttons re-render disabled is kept out synchronously.
  const busy = React.useRef(false)

  const invalidateMe = () => queryClient.invalidateQueries({ queryKey: ["me"] })

  const close = () => {
    setStep(undefined)
    setSent(undefined)
    setCode("")
    setUseRecoveryCode(false)
  }

  /*
   * One answer to a failure, keyed by the published code — never by the status:
   * only a stale sign-in is fixed by signing in again.
   */
  const onError = (error: unknown) => {
    const codes = getErrorCodes(error)
    if (codes.includes("Auth.ReauthenticationRequired")) {
      close()
      setReauthenticateOpen(true)
      return
    }
    toast.error(getErrorMessage(error))
    if (STALE_TWO_FACTOR_CODES.some((stale) => codes.includes(stale))) {
      close()
      void invalidateMe()
    }
  }

  const onSent = (
    data:
      | { maskedEmail?: string | null; sendsRemaining?: number | string | null }
      | undefined
  ) => {
    setSent({
      maskedEmail: data?.maskedEmail ?? "",
      resendAt: new Date(Date.now() + RESEND_COOLDOWN_MS),
      sendsRemaining: Number(data?.sendsRemaining ?? 0),
    })
    setCode("")
    toast.success(t("profile.emailCodeSentTo", { email: data?.maskedEmail ?? "" }))
  }

  const enableMutation = useMutation({
    gcTime: 0,
    mutationFn: async () => {
      const { data, error } = await api.POST("/api/v1/auth/2fa/email/enable")
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      setStep("confirm")
      onSent(data)
    },
    onError,
  })

  const confirmMutation = useMutation({
    // The answer may hold recovery codes: never kept in the mutation cache.
    gcTime: 0,
    mutationFn: async () => {
      const { data, error } = await api.POST("/api/v1/auth/2fa/email/confirm", {
        body: { code },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      close()
      toast.success(t("profile.emailCodeEnabledToast"))
      void invalidateMe()
      const codes = data?.recoveryCodes ?? []
      if (codes.length) setRecoveryCodes(codes.join("\n"))
    },
    onError,
  })

  const disableMutation = useMutation({
    mutationFn: async (withCode: boolean) => {
      const { data, error } = await api.POST("/api/v1/auth/2fa/email/disable", {
        body: withCode ? { code, useRecoveryCode } : {},
      })
      if (error) throw error
      // 202 with where the code went when asked without a code; 204 when switched off.
      return data as
        | { maskedEmail?: string | null; sendsRemaining?: number | string | null }
        | undefined
    },
    onSuccess: (data, withCode) => {
      if (!withCode) {
        onSent(data)
        return
      }
      close()
      toast.success(t("profile.emailCodeDisabledToast"))
      void invalidateMe()
    },
    onError,
  })

  const run = (action: () => Promise<unknown>) => {
    if (busy.current) return
    busy.current = true
    void action().finally(() => {
      busy.current = false
    })
  }

  const turnOn = () => {
    if (totpEnabled) {
      setStep("replace-warning")
      return
    }
    run(() => enableMutation.mutateAsync().catch(() => undefined))
  }

  const pending =
    enableMutation.isPending || confirmMutation.isPending || disableMutation.isPending

  const reason = !hasPassword
    ? t("profile.emailCodeNeedsPassword")
    : !available && !enabled
      ? t("profile.emailCodeUnavailable")
      : keptByPolicy && !enabled
        ? t("profile.twoFactorRequiredByPolicy")
        : null

  // An API without the feature: nothing to offer, and nothing to say.
  if (!known && !enabled) return null

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Mail className="size-4" />
          {t("profile.emailCode")}
          <Badge variant={enabled ? "default" : "secondary"}>
            {enabled ? t("common.enabled") : t("common.disabled")}
          </Badge>
        </CardTitle>
        <CardDescription>
          {enabled ? t("profile.emailCodeOnBody") : t("profile.emailCodeOffBody")}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {enabled && !available ? (
          <Alert>
            <AlertDescription>{t("profile.emailCodeOnButEmailOff")}</AlertDescription>
          </Alert>
        ) : null}
        {reason ? (
          <p className="text-sm text-muted-foreground" data-testid="email-code-reason">
            {reason}
          </p>
        ) : null}
        {enabled ? (
          <Button
            variant="outline"
            className="w-fit"
            onClick={() => setStep("disable")}
            disabled={pending}
          >
            {t("profile.emailCodeTurnOff")}
          </Button>
        ) : keptByPolicy ? null : (
          <Button
            className="w-fit"
            onClick={turnOn}
            disabled={reason !== null || pending}
          >
            {enableMutation.isPending ? <Spinner /> : null}
            {t("profile.emailCodeTurnOn")}
          </Button>
        )}
      </CardContent>

      {/* Over an authenticator app the emailed code replaces it: said before
          anything is sent. */}
      <ConfirmDialog
        open={step === "replace-warning"}
        onOpenChange={(open) => !open && close()}
        title={t("profile.emailCodeReplaceTitle")}
        description={t("profile.emailCodeReplaceBody")}
        confirmLabel={t("profile.emailCodeTurnOn")}
        loading={enableMutation.isPending}
        onConfirm={() => run(() => enableMutation.mutateAsync().catch(() => undefined))}
      />

      <ConfirmDialog
        open={step === "confirm"}
        onOpenChange={(open) => !open && close()}
        title={t("profile.emailCodeConfirmTitle")}
        description={t("profile.emailCodeSentTo", { email: sent?.maskedEmail ?? "" })}
        confirmLabel={t("auth.verify")}
        loading={confirmMutation.isPending}
        confirmDisabled={code.length !== 6}
        onConfirm={() => run(() => confirmMutation.mutateAsync().catch(() => undefined))}
      >
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="email-code-confirm">{t("auth.emailCode")}</FieldLabel>
            <Input
              id="email-code-confirm"
              value={code}
              onChange={(e) => setCode(e.target.value.trim())}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              autoFocus
            />
            <FieldDescription>{t("auth.emailCodeNewestOnly")}</FieldDescription>
          </Field>
          {sent && sent.sendsRemaining > 0 ? (
            <ResendCodeButton
              availableAt={sent.resendAt}
              pending={enableMutation.isPending}
              onResend={() => run(() => enableMutation.mutateAsync().catch(() => undefined))}
            />
          ) : null}
        </FieldGroup>
      </ConfirmDialog>

      {/* Names both effects before the code is spent. */}
      <ConfirmDialog
        open={step === "disable"}
        onOpenChange={(open) => !open && close()}
        title={t("profile.emailCodeDisableTitle")}
        description={t("profile.emailCodeDisableBody")}
        confirmLabel={t("profile.emailCodeTurnOff")}
        destructive
        loading={disableMutation.isPending && code.length > 0}
        confirmDisabled={!code}
        onConfirm={() => run(() => disableMutation.mutateAsync(true).catch(() => undefined))}
      >
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="email-code-disable">
              {useRecoveryCode ? t("auth.recoveryCode") : t("auth.emailCode")}
            </FieldLabel>
            {/* A recovery code is transcribed exactly: pinned LTR, never numeric. */}
            <Input
              id="email-code-disable"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              inputMode={useRecoveryCode ? undefined : "numeric"}
              autoComplete={useRecoveryCode ? "off" : "one-time-code"}
              dir={useRecoveryCode ? "ltr" : undefined}
              className={cn(useRecoveryCode && "font-mono")}
            />
            {!useRecoveryCode ? (
              <FieldDescription aria-live="polite">
                {sent
                  ? `${t("profile.emailCodeSentTo", { email: sent.maskedEmail })} ${t("auth.emailCodeNewestOnly")}`
                  : t("profile.emailCodeDisableHint")}
              </FieldDescription>
            ) : null}
          </Field>
          <Field orientation="horizontal" className="flex-wrap">
            {!useRecoveryCode ? (
              sent ? (
                sent.sendsRemaining > 0 ? (
                  <ResendCodeButton
                    availableAt={sent.resendAt}
                    pending={disableMutation.isPending && code.length === 0}
                    onResend={() => run(() => disableMutation.mutateAsync(false).catch(() => undefined))}
                  />
                ) : null
              ) : (
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => run(() => disableMutation.mutateAsync(false).catch(() => undefined))}
                  disabled={pending || !available}
                >
                  {t("auth.emailMeACode")}
                </Button>
              )
            ) : null}
            <Button
              type="button"
              variant="link"
              className="text-muted-foreground"
              onClick={() => {
                setUseRecoveryCode((previous) => !previous)
                setCode("")
              }}
            >
              {useRecoveryCode ? t("auth.useEmailCode") : t("auth.useRecoveryCode")}
            </Button>
          </Field>
        </FieldGroup>
      </ConfirmDialog>

      {/* Outside the card's branches: the refetch that flips the card must not
          take the codes away before they are saved. */}
      <SecretRevealDialog
        open={Boolean(recoveryCodes)}
        onOpenChange={(open) => !open && setRecoveryCodes(undefined)}
        title={t("profile.recoveryCodesTitle")}
        description={t("profile.emailCodeRecoveryCodesBody")}
        value={recoveryCodes ?? ""}
        multiline
      />

      {reauthenticateOpen ? (
        <ReauthenticateDialog
          open
          onOpenChange={setReauthenticateOpen}
          description={t("profile.twoFactorChangeNeedsRecentSignIn")}
        />
      ) : null}
    </Card>
  )
}
