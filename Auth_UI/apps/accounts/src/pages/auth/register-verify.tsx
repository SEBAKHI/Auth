import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
import {
  OTP_CODE_LENGTH,
  OtpInput,
  RESEND_COOLDOWN_MS,
} from "@authsystem/ui/common/otp-input"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Spinner } from "@authsystem/ui/spinner"

import {
  clearRegistrationFlow,
  readPendingRegistration,
  savePendingRegistration,
  setVerifiedCode,
  type PendingRegistration,
} from "./registration-flow"

/** What the completion screen says when it sends the person back here. */
interface LocationState {
  notice?: string
}

/**
 * Second of the three sign-up screens: the code that proves the mailbox.
 *
 * The code was mailed by the request that brought the person here, so this
 * screen requests nothing on mount — a request now would rotate the code that
 * is still in transit. The identity comes from session storage, which is why a
 * reload lands back on this screen instead of at the start; with no identity at
 * all there is nothing to check, and the screen sends the person to the start.
 *
 * A fresh code can be asked for once the current one has expired, and then not
 * again for a cooldown: the server allows three messages per address per
 * minute and rotates the code on each, so a button that fired freely would
 * spend that allowance on codes the person never got to read.
 */
export function RegisterVerifyPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()
  const { search } = location
  const state = location.state as LocationState | null

  const [pending, setPending] = React.useState<PendingRegistration | null>(
    readPendingRegistration
  )
  const [otp, setOtp] = React.useState("")
  const [errorMessage, setErrorMessage] = React.useState<string | null>(
    state?.notice ?? null
  )
  const [submitting, setSubmitting] = React.useState(false)
  const [requesting, setRequesting] = React.useState(false)
  // The server refused the code for good: five wrong tries spend it, and only
  // a new code can be tried again. Distinct from "expired", which the clock
  // decides.
  const [exhausted, setExhausted] = React.useState(false)
  const [cooldownUntil, setCooldownUntil] = React.useState<Date | null>(null)

  const expiresAt = React.useMemo(
    () => (pending ? new Date(pending.expiresAt) : null),
    [pending]
  )
  const expiry = useCountdown(expiresAt)
  const cooldown = useCountdown(cooldownUntil)
  const coolingDown = cooldownUntil !== null && !cooldown.expired
  const codeIsDead = expiry.expired || exhausted
  const canRequestNewCode = codeIsDead && !coolingDown && !requesting

  const submit = React.useCallback(
    async (value: string) => {
      if (!pending || value.length < OTP_CODE_LENGTH || submitting) return
      // The clock here only reports; the server decides. A browser whose
      // clock runs fast would otherwise refuse a code the server still
      // accepts, with no way past the refusal but waiting for a new code.
      setSubmitting(true)
      setErrorMessage(null)
      try {
        const { error } = await api.POST("/api/v1/Auth/registration/verify", {
          body: { pendingId: pending.pendingId, otp: value },
        })
        if (error) throw error
        // The proof stays in memory only; the next screen reads it from there.
        setVerifiedCode(value)
        navigate({ pathname: "/register/complete", search })
      } catch (error) {
        setErrorMessage(getErrorMessage(error))
        setOtp("")
        if (getErrorCodes(error).includes("EmailVerification.TooManyAttempts")) {
          setExhausted(true)
        }
      } finally {
        setSubmitting(false)
      }
    },
    [pending, submitting, navigate, search]
  )

  const requestNewCode = React.useCallback(async () => {
    if (!pending || !canRequestNewCode) return
    setRequesting(true)
    setErrorMessage(null)
    // Started before the answer arrives: a refusal (a 429, say) must not leave
    // the button ready to be hammered.
    setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_MS))
    try {
      const { data, error } = await api.POST("/api/v1/Auth/registration/start", {
        body: { email: pending.email, preferredLanguage: i18n.language },
      })
      if (error || !data) throw error ?? new Error("Registration start failed")
      const next: PendingRegistration = {
        pendingId: data.pendingId,
        email: pending.email,
        maskedEmail: data.maskedEmail,
        expiresAt: data.expiresAt,
      }
      savePendingRegistration(next)
      setPending(next)
      setOtp("")
      setExhausted(false)
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
    } finally {
      setRequesting(false)
    }
  }, [pending, canRequestNewCode, i18n.language])

  if (!pending) {
    return <Navigate to={{ pathname: "/register", search }} replace />
  }

  return (
    <AuthLayout
      title={t("auth.verifyEmailTitle")}
      subtitle={t("auth.verifyEmailDescription", { email: pending.maskedEmail })}
      footer={
        <span>
          {t("auth.haveAccount")}{" "}
          <Link
            to={{ pathname: "/login", search }}
            className="underline-offset-4 hover:underline"
          >
            {t("auth.signIn")}
          </Link>
        </span>
      }
    >
      <div className="flex flex-col items-center gap-4">
        <OtpInput
          value={otp}
          onChange={(value) => {
            setOtp(value)
            setErrorMessage(null)
          }}
          onComplete={(value) => void submit(value)}
          // Disabled only while a request is in flight. An expired code is
          // refused by `submit` with a message, not by a greyed-out field that
          // explains nothing.
          disabled={submitting}
          autoFocus
          label={t("auth.verifyEmailCodeLabel")}
        />

        {exhausted ? null : expiry.expired ? (
          <p className="text-sm text-destructive">{t("auth.codeExpired")}</p>
        ) : (
          <p className="text-sm text-muted-foreground tabular-nums">
            {t("auth.codeExpiresIn", { time: expiry.label })}
          </p>
        )}

        {errorMessage ? (
          <p role="alert" className="text-center text-sm text-destructive">
            {errorMessage}
          </p>
        ) : null}

        <Button
          className="w-full"
          // Closed only on what the server has said: five wrong tries spend
          // the code for good. An expiry read off the local clock is shown
          // above but not enforced here.
          disabled={otp.length < OTP_CODE_LENGTH || submitting || exhausted}
          onClick={() => void submit(otp)}
        >
          {submitting ? <Spinner data-icon="inline-start" /> : null}
          {t("auth.verify")}
        </Button>

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          disabled={!canRequestNewCode}
          onClick={() => void requestNewCode()}
        >
          {requesting ? <Spinner data-icon="inline-start" /> : null}
          {coolingDown
            ? t("auth.resendAvailableIn", { time: cooldown.label })
            : t("auth.requestNewCode")}
        </Button>

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          onClick={() => {
            clearRegistrationFlow()
            navigate({ pathname: "/register", search })
          }}
        >
          {t("auth.useDifferentEmail")}
        </Button>
      </div>
    </AuthLayout>
  )
}
