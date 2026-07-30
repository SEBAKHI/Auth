import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { Button } from "@astoom/ui/button"
import { Spinner } from "@astoom/ui/spinner"
import { Field, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@astoom/ui/input-otp"
import { AuthLayout } from "@astoom/ui/auth-layout"
import { getErrorCodes, getErrorMessage } from "@astoom/api/errors"
import {
  clearPendingTwoFactorChallenge,
  getPendingTwoFactorChallenge,
  useAuth,
} from "@astoom/auth/auth-context"

const CODE_LENGTH = 6

interface LocationState {
  challengeToken?: string
  from?: string
  /** Validated pending authorize URL carried over from the login page. */
  returnTo?: string | null
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

  const challengeToken =
    state?.challengeToken ?? getPendingTwoFactorChallenge()
  const from = state?.from ?? "/"
  const returnTo = state?.returnTo ?? null

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
      if (result.requiresPasswordChange) {
        navigate("/force-password-change", { replace: true })
      } else if (returnTo) {
        // Resume the pending authorize request (top-level navigation so the
        // IdP session cookie set by the verify response rides along).
        window.location.assign(returnTo)
      } else {
        navigate(from, { replace: true })
      }
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
            <Input
              id="recovery-code"
              ref={codeInputRef}
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
      </div>
    </AuthLayout>
  )
}
