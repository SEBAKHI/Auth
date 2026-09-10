import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Spinner } from "@authsystem/ui/spinner"
import { Button } from "@authsystem/ui/button"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@authsystem/ui/input-otp"

import { useLoginCompletion } from "../login-completion"

const CODE_LENGTH = 6

interface LocationState {
  /** The address the code was sent to; required to verify. */
  email?: string
  /** Masked address for display, when the caller already has it (register). */
  maskedEmail?: string
  /** Expiry of a code the caller just triggered (register), so we don't resend. */
  expiresAt?: string
}

/**
 * Standalone email-verification step. Reached right after registration (a code
 * was just sent) or from the login page when an account's email is unconfirmed
 * (no code yet, so one is requested on mount). Verifying the code confirms the
 * address and signs the user in — there is no separate manual login. A refresh
 * loses the router state and, with it, the email, so the page falls back to the
 * sign-in screen.
 *
 * Both requests answer a confirmed account exactly as they answer an unknown
 * address, so there is no "already verified" branch left to take on this
 * anonymous path: the code never arrives and the person needs the sign-in
 * screen, which the footer offers without any request having to fail first.
 */
export function VerifyEmailPage() {
  const { t } = useTranslation()
  const { completeEmailVerification } = useAuth()
  const location = useLocation()
  const state = location.state as LocationState | null

  const email = state?.email ?? ""
  const { returnTo, from, complete, challenge } = useLoginCompletion({
    defaultFrom: "/profile",
  })

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
          challenge(result.challengeToken)
          return
        }
        toast.success(t("auth.welcomeBack"))
        complete(result)
      } catch (error) {
        setErrorMessage(getErrorMessage(error))
        setOtp("")
      } finally {
        setSubmitting(false)
      }
    },
    [completeEmailVerification, countdown.expired, complete, challenge, email, expiresAt, submitting, t]
  )

  if (!email) {
    return <Navigate to="/login" replace />
  }

  const inputDisabled = !expiresAt || countdown.expired || submitting

  return (
    <AuthLayout
      title={t("auth.verifyEmailTitle")}
      subtitle={t("auth.verifyEmailDescription", { email: maskedEmail ?? email })}
      // Always offered, not only after a failure: the person on the wrong
      // account, or the owner of an already-confirmed address whom the server
      // answers as it answers a stranger, needs a way to the sign-in screen
      // that does not depend on an error arriving. The pending authorize
      // request, when there is one, rides along in the query string, and the
      // page the person was heading for rides in state, as RequireAuth sent it.
      footer={
        <Link
          to={{
            pathname: "/login",
            search: returnTo
              ? `?returnTo=${encodeURIComponent(returnTo)}`
              : "",
          }}
          state={{ from }}
          className="underline-offset-4 hover:underline"
        >
          {t("auth.backToSignIn")}
        </Link>
      }
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
