import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorCodes, getErrorMessage } from "@astoom/api/errors"
import { useAuth } from "@astoom/auth/auth-context"
import { AuthLayout } from "@astoom/ui/auth-layout"
import { Spinner } from "@astoom/ui/spinner"
import { Button } from "@astoom/ui/button"
import { useCountdown } from "@astoom/ui/hooks/use-countdown"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@astoom/ui/input-otp"

const CODE_LENGTH = 6

interface LocationState {
  /** The address the code was sent to; required to verify. */
  email?: string
  /** Masked address for display, when the caller already has it (register). */
  maskedEmail?: string
  /** Expiry of a code the caller just triggered (register), so we don't resend. */
  expiresAt?: string
  /** Where to land after auto-login; defaults to the profile. */
  from?: string
}

/**
 * Standalone email-verification step. Reached right after registration (a code
 * was just sent) or from the login page when an account's email is unconfirmed
 * (no code yet, so one is requested on mount). Verifying the code confirms the
 * address and signs the user in — there is no separate manual login. A refresh
 * loses the router state and, with it, the email, so the page falls back to the
 * sign-in screen.
 */
export function VerifyEmailPage() {
  const { t } = useTranslation()
  const { completeEmailVerification } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null

  const email = state?.email ?? ""
  const from = state?.from ?? "/profile"

  const [otp, setOtp] = React.useState("")
  const [expiresAt, setExpiresAt] = React.useState<Date | null>(() =>
    state?.expiresAt ? new Date(state.expiresAt) : null
  )
  const [maskedEmail, setMaskedEmail] = React.useState<string | null>(
    state?.maskedEmail ?? null
  )
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null)
  const [submitting, setSubmitting] = React.useState(false)
  const countdown = useCountdown(expiresAt)

  const resendMutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST(
        "/api/v1/Auth/resend-verification-email",
        { body: { email } }
      )
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      setOtp("")
      setErrorMessage(null)
      setExpiresAt(data?.expiresAt ? new Date(data.expiresAt) : null)
      setMaskedEmail(data?.maskedEmail ?? null)
    },
    onError: (error) => {
      // Already verified: nothing left to confirm, so route to a normal login.
      if (
        getErrorCodes(error).includes("EmailVerification.EmailAlreadyVerified")
      ) {
        toast.success(t("auth.emailVerifiedSuccess"))
        navigate("/login", { replace: true })
        return
      }
      setErrorMessage(getErrorMessage(error))
    },
  })

  // Request a code on mount unless the caller already handed us a live one
  // (register just sent it — resending would invalidate it and hit the rate
  // limit). The ref keeps this to a single request across re-renders.
  const { mutate: requestCode } = resendMutation
  const requestedRef = React.useRef(false)
  React.useEffect(() => {
    if (!email || requestedRef.current) return
    requestedRef.current = true
    if (!expiresAt || expiresAt.getTime() <= Date.now()) requestCode()
  }, [email, expiresAt, requestCode])

  const submit = React.useCallback(
    async (value: string) => {
      if (value.length < CODE_LENGTH || submitting) return
      if (expiresAt && countdown.expired) {
        setErrorMessage(t("auth.codeExpired"))
        return
      }
      setSubmitting(true)
      setErrorMessage(null)
      try {
        const result = await completeEmailVerification(email, value)
        if (result.status === "twoFactorRequired") {
          navigate("/two-factor", {
            replace: true,
            state: { challengeToken: result.challengeToken, from },
          })
          return
        }
        toast.success(t("auth.welcomeBack"))
        navigate(
          result.requiresPasswordChange ? "/force-password-change" : from,
          { replace: true }
        )
      } catch (error) {
        // Already verified: nothing left to confirm, so route to a normal login.
        if (
          getErrorCodes(error).includes(
            "EmailVerification.EmailAlreadyVerified"
          )
        ) {
          toast.success(t("auth.emailVerifiedSuccess"))
          navigate("/login", { replace: true })
          return
        }
        setErrorMessage(getErrorMessage(error))
        setOtp("")
      } finally {
        setSubmitting(false)
      }
    },
    [completeEmailVerification, countdown.expired, email, expiresAt, from, navigate, submitting, t]
  )

  if (!email) {
    return <Navigate to="/login" replace />
  }

  const inputDisabled = !expiresAt || countdown.expired || submitting

  return (
    <AuthLayout
      title={t("auth.verifyEmailTitle")}
      subtitle={t("auth.verifyEmailDescription", { email: maskedEmail ?? email })}
    >
      <div className="flex flex-col items-center gap-4">
        <InputOTP
          dir="ltr"
          maxLength={CODE_LENGTH}
          pattern={REGEXP_ONLY_DIGITS}
          value={otp}
          onChange={(value) => {
            setOtp(value)
            setErrorMessage(null)
          }}
          onComplete={(value: string) => void submit(value)}
          disabled={inputDisabled}
          autoFocus
          aria-label={t("auth.verifyEmailCodeLabel")}
        >
          <InputOTPGroup>
            {Array.from({ length: CODE_LENGTH }).map((_, index) => (
              <InputOTPSlot key={index} index={index} />
            ))}
          </InputOTPGroup>
        </InputOTP>

        {expiresAt ? (
          countdown.expired ? (
            <p className="text-sm text-destructive">{t("auth.codeExpired")}</p>
          ) : (
            <p className="text-sm text-muted-foreground tabular-nums">
              {t("auth.codeExpiresIn", { time: countdown.label })}
            </p>
          )
        ) : resendMutation.isPending ? (
          <Spinner className="text-muted-foreground" />
        ) : null}

        {errorMessage ? (
          <p className="text-center text-sm text-destructive">{errorMessage}</p>
        ) : null}

        <Button
          className="w-full"
          disabled={otp.length < CODE_LENGTH || inputDisabled}
          onClick={() => void submit(otp)}
        >
          {submitting ? <Spinner /> : null}
          {t("auth.verify")}
        </Button>

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          disabled={resendMutation.isPending}
          onClick={() => resendMutation.mutate()}
        >
          {resendMutation.isPending ? <Spinner /> : null}
          {t("auth.resendCode")}
        </Button>
      </div>
    </AuthLayout>
  )
}
