import { Loader2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@astoom/ui/input-otp"
import { Label } from "@astoom/ui/label"
import { AuthLayout } from "@astoom/ui/auth-layout"
import { getErrorCodes, getErrorMessage } from "@astoom/api/errors"
import {
  getPendingTwoFactorChallenge,
  useAuth,
} from "@astoom/auth/auth-context"

const CODE_LENGTH = 6

interface LocationState {
  challengeToken?: string
  from?: string
}

/**
 * Login-time two-factor step. The login endpoint issued a short-lived
 * challenge instead of tokens; this page verifies a TOTP code (or a recovery
 * code) against it and completes the session. A page refresh loses the
 * challenge on purpose — the user simply signs in again.
 */
export function TwoFactorVerifyPage() {
  const { t } = useTranslation()
  const { completeTwoFactor } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null

  const challengeToken =
    state?.challengeToken ?? getPendingTwoFactorChallenge()
  const from = state?.from ?? "/"

  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [submitting, setSubmitting] = React.useState(false)

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
    >
      <div className="flex flex-col items-center gap-4">
        {useRecoveryCode ? (
          <div className="w-full space-y-2">
            <Label htmlFor="recovery-code">{t("auth.recoveryCode")}</Label>
            <Input
              id="recovery-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              autoComplete="one-time-code"
              autoFocus
              disabled={submitting}
              className="font-mono"
            />
          </div>
        ) : (
          <InputOTP
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
          {submitting ? <Loader2 className="animate-spin" /> : null}
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
