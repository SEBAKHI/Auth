import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api, refreshSessionNow } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import type { MfaRequirement } from "@authsystem/api/mfa-requirement"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { Field, FieldDescription, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import { useAuth } from "../auth-context"
import { useLoginCompletion } from "../login-completion"
import { ReauthenticateDialog } from "../reauthenticate-dialog"
import { TwoFactorEnrollment } from "../two-factor-enrollment"

/**
 * The page between a platform administrator and the console while the server
 * withholds their platform authority (S08: `TwoFactor:EnforceForPlatformAdmins`,
 * and this session has not proved a second factor). One of three steps, as the
 * server names it:
 *
 * - enroll: set up the first factor (with the emailed code while the server asks
 *   for it — X02's flow, the profile's own component);
 * - step_up: a code from the authenticator app, or a recovery code;
 * - reauthenticate: sign in again, which records how this session was proved.
 *
 * Every step ends the same way: a refreshed token (the authority comes back only
 * in a newly minted one), the account read again, and the completion every
 * sign-in uses — back to where the user was going. Signing out is always there.
 *
 * It starts with that same refresh too: another tab may have stepped up already.
 *
 * The account is read again without ever ending the session over it: a read that
 * fails (a 5xx, a dropped connection) shows an error with "try again" instead of
 * a requirement the page can no longer trust — never a spinner that waits for
 * nothing. A session that is really over still ends through the client.
 */
export function TwoFactorRequiredPage() {
  const { t } = useTranslation()
  const { user, mfaRequirement, rereadUser, logout } = useAuth()
  const { complete } = useLoginCompletion()
  const [checking, setChecking] = React.useState(true)
  const [checkFailed, setCheckFailed] = React.useState(false)
  const started = React.useRef(false)

  /** True when the token was refreshed and the account read; false otherwise. */
  const recheck = React.useCallback(async (): Promise<boolean> => {
    try {
      await refreshSessionNow()
      return await rereadUser()
    } catch {
      return false
    }
  }, [rereadUser])

  const check = React.useCallback(async () => {
    setChecking(true)
    const ok = await recheck()
    setCheckFailed(!ok)
    setChecking(false)
  }, [recheck])

  React.useEffect(() => {
    if (started.current) return
    started.current = true
    void check()
  }, [check])

  // Nothing (left) to prove: the console, or wherever the user was going.
  React.useEffect(() => {
    if (!checking && !checkFailed && mfaRequirement === "none") complete({})
  }, [checking, checkFailed, mfaRequirement, complete])

  return (
    <AuthLayout
      title={t("auth.mfaRequiredTitle")}
      subtitle={t("auth.mfaRequiredSubtitle")}
      footer={
        <Button variant="link" className="text-muted-foreground" onClick={() => void logout()}>
          {t("common.signOut")}
        </Button>
      }
    >
      {checking ? (
        <div className="flex justify-center">
          <Spinner />
        </div>
      ) : checkFailed ? (
        <Alert variant="destructive">
          <AlertTitle>{t("auth.mfaRequiredCheckFailedTitle")}</AlertTitle>
          <AlertDescription>
            <p>{t("auth.mfaRequiredCheckFailed")}</p>
            <Button variant="secondary" size="sm" onClick={() => void check()}>
              {t("common.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : mfaRequirement === "none" ? (
        <div className="flex justify-center">
          <Spinner />
        </div>
      ) : (
        <TwoFactorRequiredStep
          requirement={mfaRequirement}
          account={{ id: user?.id, email: user?.email }}
          onDone={check}
        />
      )}
    </AuthLayout>
  )
}

function TwoFactorRequiredStep({
  requirement,
  account,
  onDone,
}: {
  requirement: Exclude<MfaRequirement, "none">
  account: { id?: string | null; email?: string | null }
  onDone: () => Promise<void>
}) {
  switch (requirement) {
    case "enroll":
      return <EnrollStep account={account} onDone={onDone} />
    case "step_up":
      return <StepUpStep onDone={onDone} />
    default:
      return <ReauthenticateStep />
  }
}

function EnrollStep({
  account,
  onDone,
}: {
  account: { id?: string | null; email?: string | null }
  onDone: () => Promise<void>
}) {
  const { t } = useTranslation()
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("auth.mfaRequiredEnroll")}</p>
      <TwoFactorEnrollment
        account={account}
        onEnabled={(codes) => {
          // The codes are shown once; the console waits until they are saved.
          if (codes.length) setRecoveryCodes(codes.join("\n"))
          else void onDone()
        }}
      />
      <SecretRevealDialog
        open={Boolean(recoveryCodes)}
        onOpenChange={(open) => {
          if (open) return
          setRecoveryCodes(undefined)
          void onDone()
        }}
        title={t("profile.recoveryCodesTitle")}
        description={t("profile.recoveryCodesBody")}
        value={recoveryCodes ?? ""}
        multiline
      />
    </div>
  )
}

function StepUpStep({ onDone }: { onDone: () => Promise<void> }) {
  const { t } = useTranslation()
  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [submitting, setSubmitting] = React.useState(false)
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)
  // A correct code is spent by the first request; a second click before the
  // button re-renders disabled would be refused as reused.
  const submittingRef = React.useRef(false)

  const submit = async () => {
    if (!code || submittingRef.current) return
    submittingRef.current = true
    setSubmitting(true)
    try {
      const { error } = await api.POST("/api/v1/auth/2fa/step-up", {
        body: { code, useRecoveryCode },
      })
      if (error) throw error
      await onDone()
    } catch (error) {
      // Keyed on the published code, never on the status: TwoFactor.LockedOut is
      // a 403 too, and only a session that cannot be upgraded signs in again.
      if (getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        setReauthenticateOpen(true)
      } else {
        toast.error(getErrorMessage(error))
      }
      setCode("")
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }

  return (
    <>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <FieldGroup>
          <Field data-disabled={submitting || undefined}>
            <FieldLabel htmlFor="step-up-code">
              {useRecoveryCode ? t("auth.recoveryCode") : t("auth.twoFactorCode")}
            </FieldLabel>
            {/* A recovery code is transcribed exactly, so it is pinned LTR like
                the sign-in page's recovery field. */}
            <Input
              id="step-up-code"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              inputMode={useRecoveryCode ? undefined : "numeric"}
              autoComplete={useRecoveryCode ? "off" : "one-time-code"}
              dir={useRecoveryCode ? "ltr" : undefined}
              disabled={submitting}
              autoFocus
            />
            <FieldDescription>{t("auth.mfaRequiredStepUp")}</FieldDescription>
          </Field>
          <Button type="submit" className="w-full" disabled={!code || submitting}>
            {submitting ? <Spinner data-icon="inline-start" /> : null}
            {t("auth.verify")}
          </Button>
          <Button
            type="button"
            variant="link"
            className="text-muted-foreground"
            onClick={() => {
              setUseRecoveryCode((previous) => !previous)
              setCode("")
            }}
          >
            {useRecoveryCode ? t("auth.useAuthenticatorCode") : t("auth.useRecoveryCode")}
          </Button>
        </FieldGroup>
      </form>

      {/* Mounted only while needed, so closing it resets it. */}
      {reauthenticateOpen ? (
        <ReauthenticateDialog
          open
          onOpenChange={setReauthenticateOpen}
          description={t("auth.mfaRequiredReauthenticateBody")}
        />
      ) : null}
    </>
  )
}

function ReauthenticateStep() {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("auth.mfaRequiredReauthenticate")}</p>
      <Button className="w-full" onClick={() => setOpen(true)}>
        {t("auth.reauthenticateAction")}
      </Button>
      {open ? (
        <ReauthenticateDialog
          open
          onOpenChange={setOpen}
          description={t("auth.mfaRequiredReauthenticateBody")}
        />
      ) : null}
    </div>
  )
}
