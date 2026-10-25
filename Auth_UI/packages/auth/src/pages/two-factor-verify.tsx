import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
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
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"

import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion } from "../login-completion"
import {
  clearPendingTwoFactorChallenge,
  getPendingTwoFactorChallenge,
  getPendingTwoFactorMethods,
  type TwoFactorMethod,
} from "../pending-challenge"
import { toAccountsSearch } from "../return-to"

const CODE_LENGTH = 6

/**
 * How long the resend waits after a send: the server's per-address window lets a
 * few through a minute, so a second click gets a countdown rather than a refusal.
 */
const RESEND_COOLDOWN_MS = 60_000

interface LocationState {
  challengeToken?: string
  methods?: readonly TwoFactorMethod[] | null
}

/** Where the last emailed code went, and what the sign-in may still ask for. */
interface EmailCodeSent {
  maskedEmail: string
  minutes?: number
  sendsRemaining: number
  resendAt: Date
}

/** Whole minutes until the code expires, by the server's expiry; 1 to 10. */
function minutesUntil(expiresAt: string | null | undefined): number | undefined {
  const milliseconds = expiresAt ? Date.parse(expiresAt) - Date.now() : NaN
  if (!Number.isFinite(milliseconds)) return undefined
  return Math.min(10, Math.max(1, Math.round(milliseconds / 60_000)))
}

/**
 * Login-time two-factor step. The login endpoint issued a short-lived
 * challenge instead of tokens; this page verifies a code against it and
 * completes the session. A page refresh loses the challenge on purpose — the
 * user simply signs in again.
 *
 * Which codes it asks for is the server's to say (S24, `twoFactorMethods`): the
 * authenticator app, or the code emailed to the account — sent only when the
 * person asks for it, never on arrival (D-T30 B) — and a recovery code either
 * way. A sign-in the server cannot email a code for (email off, or a first step
 * that already went through the mailbox) offers the recovery code alone, with
 * the way back to the password sign-in. A server that names no methods gets the
 * screen every account always saw.
 *
 * Every way out goes back to the sign-in page with the pending authorize request,
 * so a relying party's request survives the detour. The footer always offers one:
 * someone who reached this screen with the wrong account, or without their code
 * to hand, would otherwise be stuck on a page with no navigation at all.
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
  const methods =
    state?.methods ??
    (challengeToken ? getPendingTwoFactorMethods(challengeToken) : null)
  const { returnTo, complete } = useLoginCompletion({ resumePending: true })
  const { layout: appHeader } = useFlowBranding(returnTo)
  const signInAgain = { pathname: "/login", search: toAccountsSearch(returnTo) }

  const offersEmail = methods?.includes("email") === true
  const offersTotp = methods === null || methods.includes("totp")
  const primary: TwoFactorMethod = offersEmail
    ? "email"
    : offersTotp
      ? "totp"
      : "recovery"

  const [method, setMethod] = React.useState<TwoFactorMethod>(primary)
  const [code, setCode] = React.useState("")
  const [submitting, setSubmitting] = React.useState(false)
  const [refocusRequest, setRefocusRequest] = React.useState(0)
  // The server's sentence once the second factor reached the consecutive-failure
  // limit: no code is accepted any more, so the page stops asking for one and
  // names the way out instead of a toast that disappears.
  const [lockedMessage, setLockedMessage] = React.useState<string | null>(null)
  const codeInputRef = React.useRef<HTMLInputElement>(null)

  // The emailed code (S24). Each send replaces the code before it, so a second
  // click landing before the button re-renders disabled would retire the first
  // email's code: the latch is set synchronously, in the handler.
  const [emailSent, setEmailSent] = React.useState<EmailCodeSent>()
  const [sendingEmail, setSendingEmail] = React.useState(false)
  const sendingRef = React.useRef(false)
  // No code can be emailed for this sign-in any more (the per-sign-in or the
  // account's cap): the published sentence, and the way out — never a resend
  // that cannot succeed (OI-220 (1)).
  const [sendCapMessage, setSendCapMessage] = React.useState<string | null>(null)
  // The emailed code itself is refused (locked, or unavailable now): its
  // sentence, with the recovery code as the way on.
  const [emailRefusal, setEmailRefusal] = React.useState<string | null>(null)
  // The recovery codes alone reached the limit: they share the authenticator's
  // counter, while the emailed code counts apart and still signs in.
  const [recoveryRefusal, setRecoveryRefusal] = React.useState<string | null>(
    null
  )

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
    return <Navigate to={signInAgain} replace />
  }

  const locked = lockedMessage !== null
  const isEmail = method === "email"
  const isRecovery = method === "recovery"
  const emailOut = sendCapMessage !== null || emailSent?.sendsRemaining === 0

  // The challenge is single-use and short-lived; once it dies the only way
  // forward is a fresh sign-in, which keeps the relying party's request.
  const expired = () => {
    toast.error(t("auth.twoFactorChallengeExpired"))
    clearPendingTwoFactorChallenge()
    navigate(signInAgain, { replace: true })
  }

  const sendEmailCode = async () => {
    if (sendingRef.current || submitting) return
    sendingRef.current = true
    setSendingEmail(true)
    try {
      const { data, error } = await api.POST(
        "/api/v1/auth/2fa/verify/email-code",
        { body: { challengeToken } }
      )
      if (error || !data) throw error ?? new Error("Sending the code failed")

      const sent: EmailCodeSent = {
        maskedEmail: data.maskedEmail ?? "",
        minutes: minutesUntil(data.expiresAt),
        sendsRemaining: Number(data.sendsRemaining ?? 0),
        resendAt: new Date(Date.now() + RESEND_COOLDOWN_MS),
      }
      setEmailSent(sent)
      // A code typed from an earlier email could only fail now.
      setCode("")
      toast.success(t("auth.emailCodeSentTo", { email: sent.maskedEmail }))
      setRefocusRequest((n) => n + 1)
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("TwoFactor.ChallengeInvalid")) {
        expired()
        return
      }
      if (codes.includes("TwoFactor.EmailCodeTooManyRequests")) {
        setSendCapMessage(getErrorMessage(error))
        return
      }
      if (
        codes.includes("TwoFactor.EmailCodeUnavailable") ||
        codes.includes("TwoFactor.EmailCodeLocked")
      ) {
        setEmailRefusal(getErrorMessage(error))
        return
      }
      toast.error(getErrorMessage(error))
    } finally {
      sendingRef.current = false
      setSendingEmail(false)
    }
  }

  const submit = async (value: string) => {
    if (!value || submitting || locked || sendingRef.current) return
    setSubmitting(true)
    try {
      const result = await completeTwoFactor(challengeToken, value, method)
      toast.success(t("auth.welcomeBack"))
      complete(result)
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("TwoFactor.ChallengeInvalid")) {
        expired()
        return
      }
      // Too many wrong codes in a row: no code, from the app or a recovery
      // code, is accepted until the password is reset and an administrator
      // resets the factor. Keyed on the published code, never on the 403.
      if (codes.includes("TwoFactor.LockedAdminResetRequired")) {
        // On an account whose factor is the emailed code, that limit is the
        // recovery codes' alone: the page goes back to the emailed code.
        if (offersEmail && isRecovery && !emailRefusal) {
          setRecoveryRefusal(getErrorMessage(error))
          setMethod("email")
          setCode("")
          return
        }
        setLockedMessage(getErrorMessage(error))
        setCode("")
        return
      }
      // The emailed code alone is locked: a recovery code still works.
      if (codes.includes("TwoFactor.EmailCodeLocked")) {
        setEmailRefusal(getErrorMessage(error))
        setCode("")
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
    setMethod((current) => (current === "recovery" ? primary : "recovery"))
    setCode("")
  }

  const codeField = isRecovery ? (
    <Field data-disabled={submitting || locked}>
      <FieldLabel htmlFor="recovery-code">{t("auth.recoveryCode")}</FieldLabel>
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
        disabled={submitting || locked}
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
      disabled={submitting || locked}
      autoFocus
      aria-label={isEmail ? t("auth.emailCode") : t("auth.twoFactorCode")}
    >
      <InputOTPGroup>
        {Array.from({ length: CODE_LENGTH }).map((_, index) => (
          <InputOTPSlot key={index} index={index} />
        ))}
      </InputOTPGroup>
    </InputOTP>
  )

  // The emailed code is asked for only after a code was sent: there is nothing
  // to type before that.
  const showsCodeField = !isEmail || (emailSent !== undefined && !emailRefusal)

  return (
    <AuthLayout
      title={t("auth.twoFactorTitle")}
      subtitle={
        isEmail ? t("auth.emailCodeSubtitle") : t("auth.twoFactorSubtitle")
      }
      {...appHeader}
      footer={
        <div className="flex flex-col gap-1">
          <Link
            to={signInAgain}
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
        {locked ? (
          <Alert variant="destructive">
            <AlertTitle>{t("auth.twoFactorLockedTitle")}</AlertTitle>
            <AlertDescription>
              <p>{lockedMessage}</p>
              {/* The first step out. The pending authorize request rides
                  along, as on the sign-in page, so the reset keeps the
                  application's header. */}
              <Link
                to={{
                  pathname: "/forgot-password",
                  search: toAccountsSearch(returnTo),
                }}
                className="underline underline-offset-4"
                onClick={clearPendingTwoFactorChallenge}
              >
                {t("auth.forgotPassword")}
              </Link>
            </AlertDescription>
          </Alert>
        ) : null}

        {/* No code can be emailed for this sign-in: a recovery code, or the
            password sign-in, which can email one. */}
        {primary === "recovery" ? (
          <Alert>
            <AlertTitle>{t("auth.recoveryOnlyTitle")}</AlertTitle>
            <AlertDescription>
              <p>{t("auth.recoveryOnlyBody")}</p>
              <Link
                to={signInAgain}
                replace
                className="underline underline-offset-4"
                onClick={clearPendingTwoFactorChallenge}
              >
                {t("auth.signInWithPassword")}
              </Link>
            </AlertDescription>
          </Alert>
        ) : null}

        {isEmail && emailRefusal ? (
          <Alert variant="destructive">
            <AlertTitle>{t("auth.emailCodeRefusedTitle")}</AlertTitle>
            <AlertDescription>
              <p>{emailRefusal}</p>
            </AlertDescription>
          </Alert>
        ) : null}

        {recoveryRefusal ? (
          <Alert variant="destructive">
            <AlertTitle>{t("auth.twoFactorLockedTitle")}</AlertTitle>
            <AlertDescription>
              <p>{recoveryRefusal}</p>
            </AlertDescription>
          </Alert>
        ) : null}

        {isEmail && !emailRefusal && !locked ? (
          <div className="flex w-full flex-col items-center gap-2 text-center">
            {emailSent ? (
              <p className="text-sm text-muted-foreground" aria-live="polite">
                {[
                  t("auth.emailCodeSentTo", { email: emailSent.maskedEmail }),
                  emailSent.minutes === undefined
                    ? null
                    : t("auth.emailCodeExpiresIn", {
                        minutes: emailSent.minutes,
                      }),
                  t("auth.emailCodeNewestOnly"),
                ]
                  .filter(Boolean)
                  .join(" ")}
              </p>
            ) : (
              <>
                <p className="text-sm text-muted-foreground">
                  {t("auth.emailCodeIntro")}
                </p>
                <Button
                  className="w-full"
                  onClick={() => void sendEmailCode()}
                  disabled={sendingEmail || sendCapMessage !== null}
                >
                  {sendingEmail ? <Spinner /> : null}
                  {t("auth.emailMeACode")}
                </Button>
              </>
            )}
          </div>
        ) : null}

        {showsCodeField ? codeField : null}

        {showsCodeField ? (
          <Button
            className="w-full"
            disabled={
              submitting ||
              locked ||
              (isRecovery ? code.length === 0 : code.length < CODE_LENGTH)
            }
            onClick={() => void submit(code)}
          >
            {submitting ? <Spinner /> : null}
            {t("auth.verify")}
          </Button>
        ) : null}

        {/* The way on once no code can be emailed any more: the published
            sentence, and a new sign-in — never a resend that cannot succeed. */}
        {isEmail && emailOut && !emailRefusal ? (
          <Alert>
            <AlertDescription>
              <p>{sendCapMessage ?? t("auth.emailCodeNoMoreSends")}</p>
              <Link
                to={signInAgain}
                replace
                className="underline underline-offset-4"
                onClick={clearPendingTwoFactorChallenge}
              >
                {t("auth.startSignInAgain")}
              </Link>
            </AlertDescription>
          </Alert>
        ) : null}

        {isEmail && emailSent && !emailOut && !emailRefusal && !locked ? (
          <ResendCodeButton
            availableAt={emailSent.resendAt}
            pending={sendingEmail}
            onResend={() => void sendEmailCode()}
          />
        ) : null}

        {primary !== "recovery" && !recoveryRefusal ? (
          <Button
            type="button"
            variant="link"
            className="text-muted-foreground"
            onClick={toggleMode}
            disabled={locked}
          >
            {!isRecovery
              ? t("auth.useRecoveryCode")
              : offersEmail
                ? t("auth.useEmailCode")
                : t("auth.useAuthenticatorCode")}
          </Button>
        ) : null}

        {/* Folded away by default. Someone on this screen has already enrolled;
            an open block of download links would read as a prompt to install
            something mid-sign-in, which is the shape a phishing page takes. */}
        {offersTotp ? <AuthenticatorApps variant="disclosure" /> : null}
      </div>
    </AuthLayout>
  )
}
