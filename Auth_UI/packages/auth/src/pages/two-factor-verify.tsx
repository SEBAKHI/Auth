import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@authsystem/ui/input-otp"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { AuthenticatorApps } from "@authsystem/ui/common/authenticator-apps"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"

import { useLoginCompletion } from "../login-completion"
import {
  clearPendingTwoFactorChallenge,
  getPendingTwoFactorChallenge,
} from "../pending-challenge"

const CODE_LENGTH = 6

interface LocationState {
  challengeToken?: string
}

/**
 * Login-time two-factor step. The login endpoint issued a short-lived
 * challenge instead of tokens; this page verifies a TOTP code (or a recovery
 * code) against it and completes the session. A page refresh loses the
 * challenge on purpose — the user simply signs in again.
 *
 * The footer always offers a way back to sign-in: someone who reached this
 * screen with the wrong account, or without their authenticator to hand, would
 * otherwise be stuck on a page with no navigation at all.
 */
export function TwoFactorVerifyPage({
  footer,
}: {
  /** Extra footer content under the "use a different account" link. */
  footer?: React.ReactNode
} = {}) {
  const { t } = useTranslation()
  const { completeTwoFactor } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null

  // The in-memory fallbacks are paired: this is the one screen reachable after
  // the navigation state that carried both was lost, so it is the one screen
  // allowed to resume from them.
  const challengeToken =
    state?.challengeToken ?? getPendingTwoFactorChallenge()
  const { complete } = useLoginCompletion({ resumePending: true })

  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [submitting, setSubmitting] = React.useState(false)
  const [refocusRequest, setRefocusRequest] = React.useState(0)
  const codeInputRef = React.useRef<HTMLInputElement>(null)

  // Submitting disables the input, and disabling drops browser focus. After a
  // failed attempt the field is re-enabled but focus does not come back on its
  // own (autoFocus only fires on mount) — restore it so the user can retype
  // immediately instead of having to click back into the control.
  React.useEffect(() => {
    if (refocusRequest > 0 && !submitting) {
      codeInputRef.current?.focus()
    }
  }, [refocusRequest, submitting])

  if (!challengeToken) {
    return <Navigate to="/login" replace />
  }

  const submit = async (value: string) => {
    if (!value || submitting) return
    setSubmitting(true)
    try {
      const result = await completeTwoFactor(
        challengeToken,
        value,
        useRecoveryCode
      )
      toast.success(t("auth.welcomeBack"))
      complete(result)
    } catch (error) {
      // The challenge is single-use and short-lived; once it dies the only
      // way forward is a fresh password sign-in.
      if (getErrorCodes(error).includes("TwoFactor.ChallengeInvalid")) {
        toast.error(t("auth.twoFactorChallengeExpired"))
        navigate("/login", { replace: true })
        return
      }
      toast.error(getErrorMessage(error))
      setCode("")
      setRefocusRequest((n) => n + 1)
    } finally {
      setSubmitting(false)
    }
  }

  const toggleMode = () => {
    setUseRecoveryCode((prev) => !prev)
    setCode("")
  }

  return (
    <AuthLayout
      title={t("auth.twoFactorTitle")}
      subtitle={t("auth.twoFactorSubtitle")}
      footer={
        <div className="flex flex-col gap-1">
          <Link
            to="/login"
            replace
            className="underline-offset-4 hover:underline"
            onClick={clearPendingTwoFactorChallenge}
          >
            {t("auth.useDifferentAccount")}
          </Link>
          {footer}
        </div>
      }
    >
      <div className="flex flex-col items-center gap-4">
        {useRecoveryCode ? (
          <Field data-disabled={submitting}>
            <FieldLabel htmlFor="recovery-code">
              {t("auth.recoveryCode")}
            </FieldLabel>
            {/* Pinned LTR for the same reason the OTP branch below is: a recovery
                code is transcribed exactly, so it must not follow an RTL console. */}
            <Input
              id="recovery-code"
              ref={codeInputRef}
              dir="ltr"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              autoComplete="one-time-code"
              autoFocus
              disabled={submitting}
              className="font-mono"
            />
          </Field>
        ) : (
          <InputOTP
            ref={codeInputRef}
            dir="ltr"
            maxLength={CODE_LENGTH}
            pattern={REGEXP_ONLY_DIGITS}
            value={code}
            onChange={setCode}
            onComplete={(value: string) => void submit(value)}
            disabled={submitting}
            autoFocus
            aria-label={t("auth.twoFactorCode")}
          >
            <InputOTPGroup>
              {Array.from({ length: CODE_LENGTH }).map((_, index) => (
                <InputOTPSlot key={index} index={index} />
              ))}
            </InputOTPGroup>
          </InputOTP>
        )}

        <Button
          className="w-full"
          disabled={
            submitting ||
            (useRecoveryCode ? code.length === 0 : code.length < CODE_LENGTH)
          }
          onClick={() => void submit(code)}
        >
          {submitting ? <Spinner /> : null}
          {t("auth.verify")}
        </Button>

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          onClick={toggleMode}
        >
          {useRecoveryCode
            ? t("auth.useAuthenticatorCode")
            : t("auth.useRecoveryCode")}
        </Button>

        {/* Folded away by default. Someone on this screen has already enrolled;
            an open block of download links would read as a prompt to install
            something mid-sign-in, which is the shape a phishing page takes. */}
        <AuthenticatorApps variant="disclosure" />
      </div>
    </AuthLayout>
  )
}
