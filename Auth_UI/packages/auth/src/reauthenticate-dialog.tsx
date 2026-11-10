import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api, refreshSessionNow } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { OneTimeCodeField } from "@authsystem/ui/common/one-time-code-field"
import { RESEND_COOLDOWN_MS } from "@authsystem/ui/common/otp-input"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { Field, FieldDescription, FieldGroup, FieldLabel, FieldSeparator } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import { useAuth } from "./auth-context"
import { PasskeyButton } from "./passkey/passkey-button"
import { proofFailureMessage, withRetryAfter } from "./passkey/passkey-errors"
import { usePasskeySignIn, type PasskeyOutcome } from "./passkey/use-passkey-sign-in"
import type { AuthenticationResponseJSON } from "./passkey/webauthn"
import { markRecoveryCodeSignIn } from "./recovery-code-notice"
import { requestFullProof, requestPasskeySignIn } from "./sign-in-method"

/** What the server says this account must prove: the rule the proof itself is checked against. */
type Options = Schemas["ReauthenticationOptionsResponse"]

/** The names of `secondStep`, as the server sends them. */
const SECOND_STEP = {
  authenticatorApp: "AuthenticatorApp",
  emailCode: "EmailCode",
  recoveryCodeOnly: "RecoveryCodeOnly",
} as const

const REAUTHENTICATION_REQUIRED = "Auth.ReauthenticationRequired"

/** What an accepted proof spent, for a caller that holds a code of its own. */
export interface ReauthenticationProof {
  /**
   * It spent a code of the authenticator app. A code is accepted only while its time step is
   * newer than the last one accepted, so one the caller was holding will never pass now: sent
   * again, it would be counted against the factor and answered as a code someone else used.
   */
  spentAppCode: boolean
}

/**
 * The window a signed-in person proves themself in when the API answers
 * `Auth.ReauthenticationRequired` (owner decision, 2026-11-09): the proof a sign-in
 * of the same account asks for, where the person stands, with nothing signed out.
 *
 * A change to a security setting needs a proof younger than the operator's window
 * (TwoFactor:ReauthenticationMaxAgeMinutes), and a refreshed token does not count:
 * it belongs to the same old session. The API therefore answers 403 rather than 401
 * — a 401 would make the client refresh and replay, and get the same answer.
 *
 * What it asks for comes from the server (`GET auth/reauthentication`): the passkey,
 * alone; or the password, or the emailed code of an account without one, with the
 * second step that follows. So the window cannot offer what the request would refuse.
 * On success it refreshes this browser's access token, since a proof counts only for the
 * tokens issued after it (REVIEW-FIXES 152 F23), then closes and says so to the caller, once,
 * which goes on with what was asked (`onProved`), or, when the caller has nothing to resume,
 * it tells the person. A proof that lands after the person closed the window resumes nothing.
 *
 * The way out is signing in again, as it was before this window, and it stays: for
 * someone who lost what the window asks for, and for an account that has nothing to
 * prove here. The session stays until they choose it; a caller that must not sign out
 * (an application's pending request would be lost, S23) answers it (`onSignInAgain`).
 *
 * An account whose qualifying second factor is a passkey (S20, T-C7) cannot prove two
 * factors with its password or the emailed code, so a change that needs two factors
 * asks for the passkey alone (`passkey`). Removing the password needs the password
 * itself (`passwordOnly`).
 */
export function ReauthenticateDialog({
  open,
  ...props
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Why the proof is needed; the general sentence by default. */
  description?: string
  /** This change needs two factors, and the account's second factor is a passkey. */
  passkey?: boolean
  /** This change needs the password itself. */
  passwordOnly?: boolean
  /** The proof was accepted, and the window has closed: go on with what was asked. */
  onProved?: (proof: ReauthenticationProof) => void
  /** Signing in again without signing out, for a caller that would lose something by it. */
  onSignInAgain?: () => void
}) {
  // Mounted only while open, so closing it resets it: no secret outlives the window.
  return open ? <ReauthenticateWindow {...props} /> : null
}

function ReauthenticateWindow({
  onOpenChange,
  description,
  passkey = false,
  passwordOnly = false,
  onProved,
  onSignInAgain,
}: {
  onOpenChange: (open: boolean) => void
  description?: string
  passkey?: boolean
  passwordOnly?: boolean
  onProved?: (proof: ReauthenticationProof) => void
  onSignInAgain?: () => void
}) {
  const { t } = useTranslation()
  // The person asked for the way out, or the server said only a sign-in can go on.
  const [signingIn, setSigningIn] = React.useState(false)
  // Set while a proof or a sign-out is in flight: the window stays until it settles.
  const holdRef = React.useRef(false)
  // Set once the window has handed back, or gone: a proof landing later goes on with nothing,
  // and two proofs landing together resume what was asked once.
  const settledRef = React.useRef(false)
  // Set once a proof was accepted: its refresh and its hand-back run once.
  const acceptedRef = React.useRef(false)
  React.useEffect(() => {
    settledRef.current = false
    return () => {
      settledRef.current = true
    }
  }, [])

  const options = useQuery({
    queryKey: ["reauthentication-options"],
    // What the account takes changes with it, and nothing here may answer from a cache.
    gcTime: 0,
    staleTime: 0,
    retry: false,
    queryFn: () => unwrap(api.GET("/api/v1/auth/reauthentication")),
  })

  const proved = async (proof: ReauthenticationProof) => {
    if (settledRef.current || acceptedRef.current) return
    acceptedRef.current = true
    // The proof counts only for the tokens issued after it (REVIEW-FIXES 152 F23), and the token
    // this tab holds predates it: the change it asked for would be refused again, and the client
    // replays no refused change. So this browser's token is refreshed first, once, held against a
    // close; its refresh token never leaves it. A refresh that fails leaves the token as it was,
    // and the change then asks again.
    holdRef.current = true
    try {
      await refreshSessionNow().catch(() => false)
    } finally {
      holdRef.current = false
    }
    if (settledRef.current) return
    settledRef.current = true
    onOpenChange(false)
    if (onProved) onProved(proof)
    else toast.success(t("auth.reauthenticate.proved"))
  }

  const close = (next: boolean) => {
    if (!holdRef.current) onOpenChange(next)
  }

  const offers = options.data ? what(options.data, { passkey, passwordOnly }) : null
  const nothingHere =
    signingIn ||
    (options.isError && getErrorCodes(options.error).includes(REAUTHENTICATION_REQUIRED)) ||
    (offers !== null && !offers.passkey && !offers.password && !offers.emailCode)

  return (
    <Dialog open onOpenChange={close}>
      <DialogContent>
        {nothingHere ? (
          <SignInAgain
            passkey={passkey}
            holdRef={holdRef}
            onCancel={() => onOpenChange(false)}
            onSignInAgain={onSignInAgain}
          />
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>{t("auth.reauthenticate.title")}</DialogTitle>
              <DialogDescription>
                {passkey
                  ? t("auth.reauthenticate.passkeyBody")
                  : (description ?? t("auth.reauthenticate.body"))}
              </DialogDescription>
            </DialogHeader>
            {options.isPending ? (
              <div className="flex justify-center">
                <Spinner />
              </div>
            ) : options.isError ? (
              <Alert variant="destructive" data-testid="reauthenticate-options-failed">
                <AlertDescription>
                  <p>{getErrorMessage(options.error)}</p>
                  <Button variant="secondary" size="sm" onClick={() => void options.refetch()}>
                    {t("common.retry")}
                  </Button>
                </AlertDescription>
              </Alert>
            ) : (
              <Proof
                options={options.data}
                offers={offers!}
                holdRef={holdRef}
                onProved={proved}
                onMustSignIn={() => setSigningIn(true)}
                onCancel={() => onOpenChange(false)}
              />
            )}
            <Button
              type="button"
              variant="link"
              // A sentence, not a word: a button keeps one line, which a phone cannot hold.
              className="h-auto whitespace-normal text-muted-foreground"
              onClick={() => {
                // A proof on its way decides first: its way out is what follows a refusal.
                if (!holdRef.current) setSigningIn(true)
              }}
            >
              {t("auth.reauthenticate.signOutLink")}
            </Button>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

/** What this window offers: what the account takes, narrowed by what the change needs. */
function what(options: Options, { passkey, passwordOnly }: { passkey: boolean; passwordOnly: boolean }) {
  return {
    passkey: options.passkey && !passwordOnly,
    password: options.password && !passkey,
    emailCode: options.emailCode && !passkey && !passwordOnly,
  }
}

/**
 * The proofs: the passkey first, as the sign-in page offers it, then the form. The
 * ceremony lives in its own component so nothing asks for its options on an account
 * that holds no passkey.
 */
function Proof({
  options,
  offers,
  holdRef,
  onProved,
  onMustSignIn,
  onCancel,
}: {
  options: Options
  offers: ReturnType<typeof what>
  holdRef: React.RefObject<boolean>
  onProved: (proof: ReauthenticationProof) => Promise<void>
  onMustSignIn: () => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const hasForm = offers.password || offers.emailCode
  // The passkey's answer is on its way: the window's only Cancel waits for it (NIT-9), as the
  // form's waits for the form's.
  const [passkeyPending, setPasskeyPending] = React.useState(false)

  return (
    <div className="flex flex-col gap-4">
      {offers.passkey ? (
        <PasskeyProof
          variant={hasForm ? "secondary" : "default"}
          holdRef={holdRef}
          onPendingChange={setPasskeyPending}
          onProved={onProved}
          onMustSignIn={onMustSignIn}
        />
      ) : null}
      {offers.passkey && hasForm ? (
        // On the dialog's own surface, as the upstream blocks set it on a card's.
        <FieldSeparator className="*:data-[slot=field-separator-content]:bg-popover">
          {t("auth.reauthenticate.or")}
        </FieldSeparator>
      ) : null}
      {hasForm ? (
        <SecretsForm
          options={options}
          holdRef={holdRef}
          onProved={onProved}
          onMustSignIn={onMustSignIn}
          onCancel={onCancel}
        />
      ) : (
        <DialogFooter>
          <Button type="button" variant="outline" disabled={passkeyPending} onClick={onCancel}>
            {t("common.cancel")}
          </Button>
        </DialogFooter>
      )}
    </div>
  )
}

/** The passkey: the same ceremony as the sign-in page, answering a proof, not a sign-in. */
function PasskeyProof({
  variant,
  holdRef,
  onPendingChange,
  onProved,
  onMustSignIn,
}: {
  variant: "default" | "secondary"
  holdRef: React.RefObject<boolean>
  onPendingChange: (pending: boolean) => void
  onProved: (proof: ReauthenticationProof) => Promise<void>
  onMustSignIn: () => void
}) {
  const { t } = useTranslation()

  const prove = async (credential: AuthenticationResponseJSON): Promise<PasskeyOutcome> => {
    holdRef.current = true
    onPendingChange(true)
    try {
      const { error, response } = await api.POST("/api/v1/auth/reauthentication/passkey", {
        body: { credential },
      })
      if (error) throw withRetryAfter(error, response)
      holdRef.current = false
      await onProved({ spentAppCode: false })
      return { leaving: true }
    } catch (error) {
      holdRef.current = false
      if (getErrorCodes(error).includes(REAUTHENTICATION_REQUIRED)) {
        onMustSignIn()
        return { leaving: true }
      }
      return { message: proofFailureMessage(error) }
    } finally {
      onPendingChange(false)
    }
  }

  // The server said this account holds one and passkeys run: a first failure is said, in the
  // window's own words, which send a signed-in person to no sign-in (NIT-6).
  const ceremony = usePasskeySignIn({
    passkeysExpected: true,
    onCredential: prove,
    failureMessage: proofFailureMessage,
  })

  return <PasskeyButton ceremony={ceremony} label={t("auth.reauthenticate.withPasskey")} variant={variant} />
}

/**
 * The password, or the emailed code of an account without one, and the second step a
 * sign-in would ask for after it: sent together, so one request proves it all or
 * nothing.
 */
function SecretsForm({
  options,
  holdRef,
  onProved,
  onMustSignIn,
  onCancel,
}: {
  options: Options
  holdRef: React.RefObject<boolean>
  onProved: (proof: ReauthenticationProof) => Promise<void>
  onMustSignIn: () => void
  onCancel: () => void
}) {
  const { t } = useTranslation()
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [password, setPassword] = React.useState("")
  const [emailCode, setEmailCode] = React.useState("")
  const [secondCode, setSecondCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(
    options.secondStep === SECOND_STEP.recoveryCodeOnly
  )
  const [sentTo, setSentTo] = React.useState<string | null>(null)
  const [resendAt, setResendAt] = React.useState<Date | null>(null)
  const [sending, setSending] = React.useState(false)
  // A send replaces the code before it, and a proof spends it: a second click landing
  // before the buttons re-render disabled is kept out synchronously.
  const sendingRef = React.useRef(false)
  const [submitting, setSubmitting] = React.useState(false)
  const [message, setMessage] = React.useState<string | null>(null)

  const emailedSecondStep = options.secondStep === SECOND_STEP.emailCode && !useRecoveryCode
  const hasSecondStep =
    options.secondStep === SECOND_STEP.authenticatorApp ||
    options.secondStep === SECOND_STEP.emailCode ||
    options.secondStep === SECOND_STEP.recoveryCodeOnly
  const sendsCode = options.emailCode || emailedSecondStep

  const firstProofIn = options.password ? password.length > 0 : emailCode.length === 6
  const secondStepIn = !hasSecondStep || (useRecoveryCode ? secondCode.length > 0 : secondCode.length === 6)
  const canSubmit = !submitting && firstProofIn && secondStepIn

  const send = async () => {
    if (sendingRef.current || holdRef.current) return
    sendingRef.current = true
    setSending(true)
    setMessage(null)
    try {
      const { data, error } = await api.POST("/api/v1/auth/reauthentication/email-code")
      if (error || !data) throw error ?? new Error("Email code failed")
      setSentTo(data.maskedEmail ?? "")
      setEmailCode("")
      if (emailedSecondStep) setSecondCode("")
      setResendAt(new Date(Date.now() + RESEND_COOLDOWN_MS))
    } catch (error) {
      if (getErrorCodes(error).includes(REAUTHENTICATION_REQUIRED)) onMustSignIn()
      else setMessage(getErrorMessage(error))
    } finally {
      sendingRef.current = false
      setSending(false)
    }
  }

  const submit = async () => {
    if (!canSubmit || holdRef.current) return
    holdRef.current = true
    setSubmitting(true)
    setMessage(null)
    try {
      const { error } = await api.POST("/api/v1/auth/reauthentication", {
        body: {
          password: options.password ? password : null,
          emailCode: options.emailCode ? emailCode : null,
          secondStepCode: hasSecondStep ? secondCode : null,
          useRecoveryCode,
        },
      })
      if (error) throw error
      holdRef.current = false
      if (useRecoveryCode) {
        // Spent like one at sign-in: the security page says so, with the way to a new set
        // (AM-S08-1), and its count of what is left is out of date.
        markRecoveryCodeSignIn(user?.id)
        void queryClient.invalidateQueries({ queryKey: ["me"] })
      }
      // The spinner stays while the window refreshes the token, before it hands back.
      await onProved({ spentAppCode: options.secondStep === SECOND_STEP.authenticatorApp && !useRecoveryCode })
    } catch (error) {
      holdRef.current = false
      const codes = getErrorCodes(error)
      if (codes.includes(REAUTHENTICATION_REQUIRED)) {
        onMustSignIn()
        return
      }
      setMessage(getErrorMessage(error))
      // What was refused goes; what was right, or never read, stays, so it is not typed twice. The
      // second step is read only once the first proof passed (NIT-3): a wrong password, or a wrong
      // emailed code of an account without one, leaves it as it was.
      if (codes.includes("User.InvalidCurrentPassword")) setPassword("")
      else if (options.emailCode && codes.includes("TwoFactor.EmailCodeInvalid")) setEmailCode("")
      else setSecondCode("")
    } finally {
      setSubmitting(false)
    }
  }

  const sentence = sentTo !== null ? t("auth.emailCodeSentTo", { email: sentTo }) : null

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void submit()
      }}
    >
      <FieldGroup>
        {options.password ? (
          <Field>
            <FieldLabel htmlFor="reauthenticate-password">{t("auth.password")}</FieldLabel>
            <Input
              id="reauthenticate-password"
              type="password"
              autoComplete="current-password"
              autoFocus
              value={password}
              disabled={submitting}
              onChange={(event) => setPassword(event.target.value)}
            />
          </Field>
        ) : null}

        {options.emailCode && sentTo !== null ? (
          <OneTimeCodeField
            id="reauthenticate-email-code"
            label={t("profile.passkeys.emailCode")}
            value={emailCode}
            onChange={setEmailCode}
            disabled={submitting}
            autoFocus
          >
            <FieldDescription aria-live="polite">{sentence}</FieldDescription>
          </OneTimeCodeField>
        ) : null}

        {hasSecondStep && (!emailedSecondStep || sentTo !== null) ? (
          <OneTimeCodeField
            // Re-mounted on the switch, so the new field takes the focus.
            key={useRecoveryCode ? "recovery" : "code"}
            id="reauthenticate-second-step"
            label={
              useRecoveryCode
                ? t("auth.recoveryCode")
                : emailedSecondStep
                  ? t("profile.passkeys.emailCode")
                  : t("auth.twoFactorCode")
            }
            value={secondCode}
            onChange={setSecondCode}
            recovery={useRecoveryCode}
            disabled={submitting}
          >
            {emailedSecondStep ? <FieldDescription aria-live="polite">{sentence}</FieldDescription> : null}
          </OneTimeCodeField>
        ) : null}

        {sendsCode ? (
          sentTo === null ? (
            // In a div: a vertical Field stretches its direct children.
            <div>
              <Button type="button" variant="secondary" disabled={sending} onClick={() => void send()}>
                {sending ? <Spinner data-icon="inline-start" /> : null}
                {t("auth.emailMeACode")}
              </Button>
            </div>
          ) : (
            <ResendCodeButton availableAt={resendAt} pending={sending} onResend={() => void send()} />
          )
        ) : null}

        {options.secondStep === SECOND_STEP.authenticatorApp ||
        options.secondStep === SECOND_STEP.emailCode ? (
          <Button
            type="button"
            variant="link"
            className="text-muted-foreground"
            onClick={() => {
              setUseRecoveryCode((previous) => !previous)
              setSecondCode("")
            }}
          >
            {useRecoveryCode
              ? options.secondStep === SECOND_STEP.emailCode
                ? t("profile.passkeys.emailCode")
                : t("auth.useAuthenticatorCode")
              : t("auth.useRecoveryCode")}
          </Button>
        ) : null}

        {message ? (
          <Alert variant="destructive" data-testid="reauthenticate-message">
            <AlertDescription>{message}</AlertDescription>
          </Alert>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="outline" disabled={submitting} onClick={onCancel}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" disabled={!canSubmit}>
            {submitting ? <Spinner data-icon="inline-start" /> : null}
            {t("auth.reauthenticate.confirm")}
          </Button>
        </DialogFooter>
      </FieldGroup>
    </form>
  )
}

/**
 * The way out: sign out, and the route guard sends the user to the sign-in page carrying
 * the page they were on (the security tab is in its URL), and the sign-in completion brings
 * them straight back to it. Cancel leaves everything as it was.
 *
 * The sign-out button is held until the sign-out has finished.
 */
function SignInAgain({
  passkey,
  holdRef,
  onCancel,
  onSignInAgain,
}: {
  passkey: boolean
  holdRef: React.RefObject<boolean>
  onCancel: () => void
  onSignInAgain?: () => void
}) {
  const { t } = useTranslation()
  const { logout } = useAuth()
  const [signingOut, setSigningOut] = React.useState(false)

  const signInAgain = async () => {
    if (holdRef.current) return
    holdRef.current = true
    setSigningOut(true)
    // Rides through the sign-out and the redirect: a sign-in by code from the page they
    // land on proves the password too where the account has one, or the change would ask
    // again (OI-215). For a passkey account the page offers the passkey first (T-C7).
    if (passkey) requestPasskeySignIn()
    else requestFullProof()
    try {
      if (onSignInAgain) onSignInAgain()
      else await logout()
    } finally {
      holdRef.current = false
      setSigningOut(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t("auth.reauthenticateTitle")}</DialogTitle>
        <DialogDescription>
          {passkey ? t("auth.reauthenticatePasskeyBody") : t("auth.reauthenticateBody")}
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button type="button" variant="outline" disabled={signingOut} onClick={onCancel}>
          {t("common.cancel")}
        </Button>
        <Button type="button" onClick={() => void signInAgain()} disabled={signingOut}>
          {signingOut ? <Spinner data-icon="inline-start" /> : null}
          {passkey ? t("auth.reauthenticateWithPasskeyAction") : t("auth.reauthenticateAction")}
        </Button>
      </DialogFooter>
    </>
  )
}
