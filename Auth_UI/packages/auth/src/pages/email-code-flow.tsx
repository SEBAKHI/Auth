import { zodResolver } from "@hookform/resolvers/zod"
import { Info } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
import {
  OTP_CODE_LENGTH,
  OtpInput,
  RESEND_COOLDOWN_MS,
} from "@authsystem/ui/common/otp-input"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import { useAuth, type EmailCodeSignInResult } from "../auth-context"
import { detectedTimeZone } from "../detected-time-zone"
import {
  startEmailCode,
  verifyEmailCode,
  type EmailCodeNextStep,
  type PendingEmailCode,
} from "../email-code"
import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion, type CompletionOptions } from "../login-completion"
import { offerCredential } from "../offer-credential"
import { PasswordField } from "../password-field"
import { applyPasswordServerErrors, passwordSchema } from "../password-rules"
import { rememberMethodForChallenge, signInEnded } from "../sign-in-method"

/**
 * Everything after a code was sent from the sign-in page (OI-215): the code,
 * then what the server says comes next once it matched.
 *
 * The code screen is the same for every address — the same sentence, the same
 * field, the same resend — because the page does not know, and must not show,
 * whether the address has an account. Only after the code matched does the
 * server answer a step, and only the mailbox's holder can see that answer:
 *
 * - "signIn": the code is spent to sign in; the answer is a session, a second
 *   step (the authenticator app), or the password (the account holds platform
 *   authority, or the "sign in again" dialog asked for full proof);
 * - "completeAccount": a new account, with the fields the deployment asks for —
 *   none, and it is created at once;
 * - "recover": the account waits for deletion and may be brought back.
 *
 * Its own module so the sign-in page loads it only when a code is sent.
 */
export function EmailCodeFlow({
  pending: initialPending,
  staySignedIn,
  requireFullProof,
  pageFooter,
  onChangeAddress,
  onPasswordRequired,
  onPendingChange,
  onEnded,
  completionOptions,
  startCode = startEmailCode,
}: {
  pending: PendingEmailCode
  /** The sign-in page's "Stay signed in" answer, false where the box is not offered. */
  staySignedIn: boolean
  /** The "sign in again" dialog's add-only reason. */
  requireFullProof: boolean
  pageFooter?: React.ReactNode
  /** Back to the address, forgetting the code. */
  onChangeAddress: () => void
  /** The sign-in needs the account's password: the page asks for it. */
  onPasswordRequired: (email: string) => void
  /** A new code was sent (the sign-up keeps it across a reload). */
  onPendingChange?: (pending: PendingEmailCode) => void
  /** The flow ended in a session or a second step. */
  onEnded?: () => void
  /** What this sign-in adds to the completion rule (an invitation's landing). */
  completionOptions?: CompletionOptions
  /** How a new code is asked for: the sign-up passes its own start. */
  startCode?: (email: string, preferredLanguage: string) => Promise<PendingEmailCode>
}) {
  const { t } = useTranslation()
  const { signInWithEmailCode, recoverWithEmailCode, completeRegistration } =
    useAuth()
  const { returnTo, complete, challenge } = useLoginCompletion()
  const { layout: appHeader } = useFlowBranding(returnTo)

  const [pending, setPending] = React.useState(initialPending)
  const [stage, setStage] = React.useState<
    | { kind: "code"; notice: string | null }
    | { kind: "complete"; otp: string; next: EmailCodeNextStep }
    | { kind: "recover"; otp: string }
  >({ kind: "code", notice: null })

  // A refused code returns to the code screen with the reason, never to a
  // dead end: retyping or asking for a new code is the way forward.
  const backToCode = (error: unknown) => {
    setStage({ kind: "code", notice: getErrorMessage(error) })
  }

  const signIn = (otp: string) =>
    signInWithEmailCode({
      pendingId: pending.pendingId,
      otp,
      requireFullProof,
      staySignedIn,
    })

  // Every sign-in this flow ends: the code was the first proof. A failure is
  // the caller's to show.
  const finish = async (
    result: EmailCodeSignInResult,
    otp: string,
    welcome: string
  ): Promise<void> => {
    if (result.status === "twoFactorRequired") {
      onEnded?.()
      rememberMethodForChallenge(result.challengeToken, "code")
      challenge(result.challengeToken, result.methods)
      return
    }
    if (result.status === "authenticated") {
      onEnded?.()
      signInEnded("code")
      toast.success(welcome)
      complete(result, completionOptions)
      return
    }
    await follow(otp, result.nextStep)
  }

  // What the server said comes next, after the code matched.
  const follow = async (otp: string, next: EmailCodeNextStep) => {
    switch (next.step) {
      case "signIn":
        await finish(await signIn(otp), otp, t("auth.welcomeBack"))
        return
      case "passwordRequired":
        onPasswordRequired(pending.email)
        return
      case "recover":
        setStage({ kind: "recover", otp })
        return
      case "completeAccount":
        if (next.passwordRequired || next.nameRequired) {
          setStage({ kind: "complete", otp, next })
          return
        }
        // Nothing to ask: the account is created at once.
        await finish(
          await completeRegistration({
            pendingId: pending.pendingId,
            otp,
            timeZone: detectedTimeZone(),
          }),
          otp,
          t("auth.accountCreated")
        )
        return
    }
  }

  if (stage.kind === "complete") {
    return (
      <CompleteAccountStage
        key={stage.otp}
        pending={pending}
        next={stage.next}
        appHeader={appHeader}
        pageFooter={pageFooter}
        onChangeAddress={onChangeAddress}
        onCodeRefused={backToCode}
        onCreate={async (fields) => {
          const result = await completeRegistration({
            pendingId: pending.pendingId,
            otp: stage.otp,
            ...fields,
            timeZone: detectedTimeZone(),
          })
          if (result.status === "authenticated" && fields.password) {
            await offerCredential(pending.email, fields.password)
          }
          await finish(result, stage.otp, t("auth.accountCreated"))
        }}
        onAccountExists={(error) => {
          // Created by another door while the code was typed: the code is still
          // the way in, so the person starts over at the address.
          toast.info(getErrorMessage(error))
          onChangeAddress()
        }}
      />
    )
  }

  if (stage.kind === "recover") {
    return (
      <RecoverStage
        appHeader={appHeader}
        pageFooter={pageFooter}
        onChangeAddress={onChangeAddress}
        onRecover={async (twoFactorCode) => {
          const result = await recoverWithEmailCode({
            pendingId: pending.pendingId,
            otp: stage.otp,
            twoFactorCode,
            requireFullProof,
          })
          await finish(result, stage.otp, t("accountDeletion.restored"))
        }}
        onCodeRefused={backToCode}
      />
    )
  }

  return (
    <CodeStage
      pending={pending}
      notice={stage.notice}
      appHeader={appHeader}
      pageFooter={pageFooter}
      onChangeAddress={onChangeAddress}
      startCode={startCode}
      onResent={(next) => {
        setPending(next)
        onPendingChange?.(next)
      }}
      onVerified={follow}
    />
  )
}

type AppHeader = ReturnType<typeof useFlowBranding>["layout"]

/**
 * The code. The same sentence for every address, the typed address shown back,
 * and a new code offered a minute after the last one: the server allows three
 * messages per address per minute and rotates the code on each, so a button
 * that fired freely would spend that allowance on codes nobody read.
 */
function CodeStage({
  pending,
  notice,
  appHeader,
  pageFooter,
  onChangeAddress,
  onResent,
  onVerified,
  startCode,
}: {
  pending: PendingEmailCode
  notice: string | null
  appHeader: AppHeader
  pageFooter?: React.ReactNode
  onChangeAddress: () => void
  onResent: (pending: PendingEmailCode) => void
  onVerified: (otp: string, next: EmailCodeNextStep) => Promise<void>
  startCode: (email: string, preferredLanguage: string) => Promise<PendingEmailCode>
}) {
  const { t, i18n } = useTranslation()
  const [otp, setOtp] = React.useState("")
  const [errorMessage, setErrorMessage] = React.useState<string | null>(notice)
  const [submitting, setSubmitting] = React.useState(false)
  const submittingRef = React.useRef(false)
  const [requesting, setRequesting] = React.useState(false)
  // Five wrong tries spend the code for good; only a new one can be tried.
  const [exhausted, setExhausted] = React.useState(false)
  // The address's wrong codes for the day (D-110-2): a new code would be refused
  // too, so none is offered; the password and "forgot password" are one step
  // back, behind "use a different email".
  const [limitReached, setLimitReached] = React.useState(false)
  // The code was just sent: the next one is a minute away, or sooner when it
  // expires first (a reload long after the send finds it already expired).
  const [cooldownUntil, setCooldownUntil] = React.useState(
    () =>
      new Date(
        Math.min(Date.now() + RESEND_COOLDOWN_MS, Date.parse(pending.expiresAt))
      )
  )

  const expiresAt = React.useMemo(
    () => new Date(pending.expiresAt),
    [pending.expiresAt]
  )
  const expiry = useCountdown(expiresAt)
  const cooldown = useCountdown(cooldownUntil)
  // Not while a code is being checked: the new code would void it mid-request.
  const canResend = cooldown.expired && !requesting && !submitting && !limitReached

  const submit = async (value: string) => {
    // A spent code (its own five tries, or the address's for the day) is never
    // sent again from here: the six boxes still auto-submit on the sixth digit.
    if (value.length < OTP_CODE_LENGTH || submittingRef.current || exhausted) return
    // The clock here only reports; the server decides.
    submittingRef.current = true
    setSubmitting(true)
    setErrorMessage(null)
    try {
      const next = await verifyEmailCode(pending.pendingId, value)
      await onVerified(value, next)
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
      setOtp("")
      // Five wrong tries of this code, or the address's wrong codes for the day
      // (D-110-2): either way this code is done; the server's text says what next.
      const codes = getErrorCodes(error)
      const limit = codes.includes("EmailVerification.WrongCodeLimitReached")
      if (limit || codes.includes("EmailVerification.TooManyAttempts")) {
        setExhausted(true)
      }
      if (limit) setLimitReached(true)
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }

  const resend = async () => {
    if (!canResend) return
    setRequesting(true)
    setErrorMessage(null)
    // Started before the answer: a refusal must not leave the button ready to
    // be hammered.
    setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_MS))
    try {
      onResent(await startCode(pending.email, i18n.language))
      // The new code voids the old one, and its five tries are new too.
      setOtp("")
      setExhausted(false)
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
    } finally {
      setRequesting(false)
    }
  }

  return (
    <AuthLayout
      title={t("auth.emailCodeTitle")}
      subtitle={t("auth.emailCodeMessageSent", { email: pending.email })}
      pageFooter={pageFooter}
      {...appHeader}
    >
      <div className="flex flex-col items-center gap-4">
        <OtpInput
          value={otp}
          onChange={(value) => {
            setOtp(value)
            setErrorMessage(null)
          }}
          onComplete={(value) => void submit(value)}
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
          disabled={!canResend}
          onClick={() => void resend()}
        >
          {requesting ? <Spinner data-icon="inline-start" /> : null}
          {cooldown.expired
            ? t("auth.requestNewCode")
            : t("auth.resendAvailableIn", { time: cooldown.label })}
        </Button>

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          onClick={onChangeAddress}
        >
          {t("auth.useDifferentEmail")}
        </Button>
      </div>
    </AuthLayout>
  )
}

/**
 * «Complete your account»: the fields the deployment asks a new account for,
 * after the code proved the address. Never shown with none to ask: the account
 * is then created straight from the code.
 */
function CompleteAccountStage({
  pending,
  next,
  appHeader,
  pageFooter,
  onChangeAddress,
  onCodeRefused,
  onCreate,
  onAccountExists,
}: {
  pending: PendingEmailCode
  next: EmailCodeNextStep
  appHeader: AppHeader
  pageFooter?: React.ReactNode
  onChangeAddress: () => void
  onCodeRefused: (error: unknown) => void
  onCreate: (fields: {
    password?: string
    firstName?: string
    lastName?: string
  }) => Promise<void>
  onAccountExists: (error: unknown) => void
}) {
  const { t } = useTranslation()
  const { policy } = usePasswordPolicy()
  const askName = next.nameRequired
  const askPassword = next.passwordRequired
  const [phase, setPhase] = React.useState<"form" | "submitting">("form")

  const schema = z.object({
    firstName: askName
      ? z.string().trim().min(1, t("validation.required"))
      : z.string(),
    lastName: askName
      ? z.string().trim().min(1, t("validation.required"))
      : z.string(),
    password: askPassword ? passwordSchema(policy) : z.string(),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: "", lastName: "", password: "" },
  })

  const create = async (values: z.infer<typeof schema>) => {
    setPhase("submitting")
    try {
      await onCreate({
        password: askPassword ? values.password : undefined,
        firstName: askName ? values.firstName : undefined,
        lastName: askName ? values.lastName : undefined,
      })
    } catch (error) {
      setPhase("form")
      const codes = getErrorCodes(error)
      if (codes.some((code) => code.startsWith("EmailVerification."))) {
        onCodeRefused(error)
        return
      }
      if (
        codes.includes("User.DuplicateEmail") ||
        codes.includes("User.AccountCreatedSignInRequired")
      ) {
        onAccountExists(error)
        return
      }
      if (!askPassword || !applyPasswordServerErrors(form, "password", error)) {
        toast.error(getErrorMessage(error))
      }
    }
  }

  return (
    <AuthLayout
      title={t("auth.completeAccountTitle")}
      subtitle={t("auth.completeAccountSubtitle")}
      pageFooter={pageFooter}
      {...appHeader}
    >
      <Form {...form}>
          <form onSubmit={form.handleSubmit(create)}>
            <FieldGroup>
              <Field>
                <FieldLabel htmlFor="complete-account-email">
                  {t("auth.email")}
                </FieldLabel>
                {/* The real address in the username field: the field password
                    managers pair a new password with. */}
                <Input
                  id="complete-account-email"
                  type="email"
                  name="email"
                  autoComplete="username"
                  value={pending.email}
                  readOnly
                  tabIndex={-1}
                  dir="ltr"
                />
                <FieldDescription>
                  <Button
                    type="button"
                    variant="link"
                    className="h-auto p-0"
                    onClick={onChangeAddress}
                  >
                    {t("auth.changeEmail")}
                  </Button>
                </FieldDescription>
              </Field>
              {askName ? (
                <>
                  <FormField
                    control={form.control}
                    name="firstName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t("auth.firstName")}</FormLabel>
                        <FormControl>
                          <Input autoComplete="given-name" autoFocus {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="lastName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>{t("auth.lastName")}</FormLabel>
                        <FormControl>
                          <Input autoComplete="family-name" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              ) : null}
              {askPassword ? (
                <PasswordField
                  control={form.control}
                  name="password"
                  label={t("auth.password")}
                  autoFocus={!askName}
                />
              ) : null}
              <Button
                type="submit"
                className="w-full"
                disabled={phase === "submitting"}
              >
                {phase === "submitting" ? (
                  <>
                    <Spinner data-icon="inline-start" />
                    {t("auth.creatingAccount")}
                  </>
                ) : (
                  t("auth.createAccount")
                )}
              </Button>
            </FieldGroup>
          </form>
        </Form>
    </AuthLayout>
  )
}

/**
 * An account waiting for deletion, proven by the code: one button brings it
 * back and signs in. The authenticator app is asked when the server says the
 * account has one; the emailed code is spent only after the app's code was
 * accepted, so a mistyped app code is simply typed again.
 */
function RecoverStage({
  appHeader,
  pageFooter,
  onChangeAddress,
  onRecover,
  onCodeRefused,
}: {
  appHeader: AppHeader
  pageFooter?: React.ReactNode
  onChangeAddress: () => void
  onRecover: (twoFactorCode: string | undefined) => Promise<void>
  onCodeRefused: (error: unknown) => void
}) {
  const { t } = useTranslation()
  const [askTwoFactor, setAskTwoFactor] = React.useState(false)
  const [twoFactorCode, setTwoFactorCode] = React.useState("")
  const [submitting, setSubmitting] = React.useState(false)
  const submittingRef = React.useRef(false)
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null)

  const recover = async () => {
    if (submittingRef.current) return
    if (askTwoFactor && twoFactorCode.trim().length === 0) {
      setErrorMessage(t("validation.required"))
      return
    }
    submittingRef.current = true
    setSubmitting(true)
    setErrorMessage(null)
    try {
      await onRecover(askTwoFactor ? twoFactorCode.trim() : undefined)
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("User.TwoFactorRequired")) {
        setAskTwoFactor(true)
      } else if (codes.some((code) => code.startsWith("EmailVerification."))) {
        onCodeRefused(error)
      } else {
        setErrorMessage(getErrorMessage(error))
      }
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title={t("accountDeletion.recoveryTitle")}
      pageFooter={pageFooter}
      {...appHeader}
    >
      <FieldGroup>
        <Alert>
          <Info />
          <AlertDescription>{t("accountDeletion.recoveryFallback")}</AlertDescription>
        </Alert>
        {askTwoFactor ? (
          <Field>
            <FieldLabel htmlFor="recover-two-factor">
              {t("accountDeletion.twoFactorCode")}
            </FieldLabel>
            <Input
              id="recover-two-factor"
              autoComplete="one-time-code"
              autoFocus
              value={twoFactorCode}
              onChange={(event) => setTwoFactorCode(event.target.value)}
            />
            <FieldDescription>
              {t("accountDeletion.twoFactorCodeAppOrRecovery")}
            </FieldDescription>
          </Field>
        ) : null}
        {errorMessage ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage}
          </p>
        ) : null}
        <Button
          className="w-full"
          disabled={submitting}
          onClick={() => void recover()}
        >
          {submitting ? <Spinner data-icon="inline-start" /> : null}
          {t("accountDeletion.restoreAccount")}
        </Button>
        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          onClick={onChangeAddress}
        >
          {t("auth.useDifferentEmail")}
        </Button>
      </FieldGroup>
    </AuthLayout>
  )
}
