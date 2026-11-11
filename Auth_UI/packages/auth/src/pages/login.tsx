import { zodResolver } from "@hookform/resolvers/zod"
import { Info, KeyRound, ShieldCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { Checkbox } from "@authsystem/ui/checkbox"
import {
  Field,
  FieldContent,
  FieldGroup,
  FieldLegend,
  FieldSet,
  FieldTitle,
} from "@authsystem/ui/field"
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { useAuth } from "@authsystem/auth/auth-context"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { isCookieBlocked } from "@authsystem/api/token-store"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { useBranding, type SignInMethod } from "@authsystem/ui/branding"

import { startEmailCode, type PendingEmailCode } from "../email-code"
import { isAbsoluteUrl } from "../external/recovery-navigation"
import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion } from "../login-completion"
import { PasskeyButton } from "../passkey/passkey-button"
import { passkeyFailureMessage } from "../passkey/passkey-errors"
import { notePasskeysOff } from "../passkey/passkey-offer"
import { PasskeyRequiredProvider, passkeyRequiredNotice } from "../passkey/passkey-required"
import { usePasskeySignIn, type PasskeyOutcome } from "../passkey/use-passkey-sign-in"
import type { AuthenticationResponseJSON } from "../passkey/webauthn"
import { lookUpPasskey, type PasskeyLookup } from "../passkey-lookup"
import { FullScreenLoader } from "../require-auth"
import { toAccountsSearch } from "../return-to"
import { canSkipSignIn } from "../sign-in-marker"
import type { SignInReasonWord } from "../sign-in-reason"
import {
  isFullProofRequested,
  isPasskeySignInRequested,
  passkeySignInEnded,
  primarySignInMethod,
  readLastSignInMethod,
  rememberMethodForChallenge,
  signInEnded,
} from "../sign-in-method"
import { StaySignedInProvider } from "../stay-signed-in"
import { Spinner } from "@authsystem/ui/spinner"

// The code screens are loaded when a code is sent, not with the sign-in page:
// the six-box field and the account-completion form weigh on every visit
// otherwise (login-payload.spec.ts measures that payload).
const EmailCodeFlow = React.lazy(() =>
  import("./email-code-flow").then((module) => ({
    default: module.EmailCodeFlow,
  }))
)

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** What the page says for each sign-in reason (S23): a short title, then why. */
const REASON_TEXT: Record<SignInReasonWord, { title: string; body: string }> = {
  mfa_step_up: {
    title: "auth.signInReasonStepUpTitle",
    body: "auth.signInReasonStepUpBody",
  },
  mfa_enroll: {
    title: "auth.signInReasonEnrollTitle",
    body: "auth.signInReasonEnrollBody",
  },
  mfa_reauthenticate: {
    title: "auth.signInReasonReauthenticateTitle",
    body: "auth.signInReasonReauthenticateBody",
  },
  // S20: the account's one qualifying factor is a passkey. The body names the way
  // out for a lost one (sign in, remove it in Profile › Security, add another).
  mfa_passkey: {
    title: "auth.signInReasonPasskeyTitle",
    body: "auth.signInReasonPasskeyBody",
  },
}

/** Where a person who lost their passkey removes it: Profile › Security. */
const LOST_PASSKEY_DESTINATION = { pathname: "/profile", search: "?tab=security" }

/** The passkey screen's list of other ways, which "Try another way" controls. */
const OTHER_WAYS_ID = "passkey-other-ways"

interface LocationState {
  email?: string
  /**
   * The method to open with: a sign-in by code from another screen that needs
   * the password lands here with the password first, not with a second code.
   */
  method?: SignInMethod
}

/**
 * Where the page stands. "credentials" is the address with the password or the
 * code first; "passkey" is the passkey screen, for an address the server said
 * has one (OI-280); "code" is everything after a code was sent; "password" is
 * the password step: after the address while passkeys may run here (OI-280,
 * `afterCode` false), or the password a sign-in by code still needs (the account
 * holds platform authority, or the "sign in again" dialog asked for full proof).
 */
type Step =
  | { kind: "credentials" }
  | { kind: "passkey"; email: string }
  | { kind: "code"; pending: PendingEmailCode }
  | { kind: "password"; email: string; afterCode: boolean }

export function LoginPage({
  providers,
  footer,
  pageFooter,
  subtitle,
  recoveryPath,
}: {
  /** External sign-in options rendered under the credentials form. */
  providers?: React.ReactNode
  /**
   * Extra content under the card (e.g. a create-account link). Shown while the
   * password comes first: with the code first, the same form creates an account.
   */
  footer?: React.ReactNode
  /** Ambient links pinned to the bottom of the page (e.g. privacy policy). */
  pageFooter?: React.ReactNode
  /** Overrides the console-flavored default subtitle. */
  subtitle?: string
  /**
   * Where to send an account that is pending deletion — a route in this app, or
   * an absolute URL when the recovery screen lives on another origin.
   *
   * This used to be the literal "/account-recovery", which is a route only the
   * accounts app has: in the console the same branch fell through the router to
   * the catch-all and rendered a 404, so valid credentials for a recoverable
   * account produced a not-found page. A prop, like the one the provider
   * buttons already took.
   */
  recoveryPath?: string
} = {}) {
  const { t, i18n } = useTranslation()
  const { login, loginWithPasskey, status } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null
  const presetEmail = state?.email ?? ""

  // Pending OAuth authorize request (hosted-login flow): strictly validated —
  // only the auth origin's authorize endpoint is ever a legal destination.
  const { returnTo, reason, complete, challenge, interstitial } =
    useLoginCompletion()
  // One sign-in, not two (OI-275): the server sent this tab back to set up an
  // authenticator for the request a sign-in here resumed moments ago, for the
  // same account and session. The form is skipped and the completion rule
  // opens the set-up page, spending the marker; any condition false is the
  // form. Decided while rendering, so the form is never painted; handed over
  // once, so a second effect run (StrictMode) adds nothing.
  const skipSignIn = React.useMemo(
    () => status === "authenticated" && canSkipSignIn(returnTo, reason),
    [status, returnTo, reason]
  )
  const handedOver = React.useRef(false)
  React.useEffect(() => {
    if (!skipSignIn || handedOver.current) return
    handedOver.current = true
    complete({})
  }, [skipSignIn, complete])
  // The last session ended at its first refresh because the browser withheld
  // the sign-in cookie from the API host. Without saying so, "you were signed
  // out" would repeat on every sign-in with nothing to act on.
  const [cookieBlocked] = React.useState(isCookieBlocked)
  const { appBranding, layout: appHeader } = useFlowBranding({ returnTo })
  // Hidden when the deployment's administrator decides for everyone (OI-165).
  // The server applies that decision whatever this page sends.
  const { staySignedInOffered, signInOptions } = useBranding()
  // OI-215. Before the code, only what the visitor already knows decides the
  // page's method: the deployment's options, the method that last worked on
  // this browser, and their own click. When an application's two-step
  // requirement sent the person back (S23), the server's word for the account
  // comes before the remembered one (D-S23-3 (c)). The one thing the page asks
  // about the address is whether it has a passkey, after "Continue" (OI-280,
  // D-S20-10), and only while passkeys may run here.
  const options = signInOptions ?? null
  const codeOffered = options?.codeSignInOffered === true
  const [remembered] = React.useState(readLastSignInMethod)
  const [chosen, setChosen] = React.useState<SignInMethod | null>(
    state?.method === "password" ? "password" : null
  )
  const method: SignInMethod =
    options && codeOffered
      ? (chosen ??
        primarySignInMethod(options, remembered, reason?.method ?? null))
      : "password"
  // Full proof: a sign-in by code proves the password too where the account
  // has one. Asked by the "sign in again" dialog, and under every sign-in
  // reason (S23): a password account that takes the code link must not come
  // back with the same requirement unmet. An account without a password is
  // never asked, so a forged reason costs its forger a password prompt.
  const [fullProofAsked] = React.useState(isFullProofRequested)
  const fullProof = fullProofAsked || reason !== null
  const [step, setStep] = React.useState<Step>({ kind: "credentials" })
  // OI-280: the lookup answered that passkeys do not run here; it is not asked
  // again for the rest of this page's life, which is then the page as before.
  const [lookupOff, setLookupOff] = React.useState(false)
  // The lookup on its way, as the press's own handle (REVIEW 159 F1, F2). While
  // it is out, the question is on its way, not a code, so the button keeps its
  // words. Leaving the page, another step or the other method gives it up: its
  // answer is then dropped, and nothing is sent.
  const [pendingLookup, setPendingLookup] =
    React.useState<AbortController | null>(null)
  React.useEffect(() => {
    if (!pendingLookup) return
    return () => pendingLookup.abort()
  }, [pendingLookup, method, step.kind])
  const lookingUp = pendingLookup !== null
  // S20 (D-S20-4 (ii-s)): a door answered that this account signs in with its
  // passkey. Its sentence stays on the page, above the passkey button, which
  // takes the focus; never a toast that goes.
  const [passkeyNotice, setPasskeyNotice] = React.useState<string | null>(null)
  // The "sign in again" dialog of a passkey account (T-C7), and an application's
  // requirement only the passkey meets (`mfa_passkey`), put the passkey first.
  const [passkeyAsked] = React.useState(isPasskeySignInRequested)
  const passkeyButtonRef = React.useRef<HTMLButtonElement>(null)
  const passkeyScreenButtonRef = React.useRef<HTMLButtonElement>(null)
  // "Try another way" on the passkey screen (D-S20-16): closed until asked.
  const [otherWaysOpen, setOtherWaysOpen] = React.useState(false)
  // Each arrival on the passkey screen, counted by "Continue" (OI-280 PR B,
  // T-B14): the window opens by itself once per arrival.
  const [passkeyArrival, setPasskeyArrival] = React.useState(0)
  // The list's code item sends an email: one at a time, decided in the click.
  const sendingFromListRef = React.useRef(false)
  const [sendingFromList, setSendingFromList] = React.useState(false)

  const passwordToFinish = step.kind === "password" && step.afterCode
  const passwordStep = step.kind === "password" && !step.afterCode

  // The password is required where it is shown; that check is the submit's,
  // since the form keeps the resolver it was made with.
  const schema = z.object({
    email: z
      .string()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
    password: z.string(),
    staySignedIn: z.boolean(),
  })

  // "Stay signed in" (OI-154) starts unticked on every visit and is never kept
  // anywhere: a choice remembered in the browser would tick itself for the next
  // person at a shared computer.
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: presetEmail, password: "", staySignedIn: false },
  })

  // The provider buttons under the form read the box when they sign in. A box
  // that is not shown sends false, even if it was ticked before it went away.
  // Read through a ref so the reader stays one function: the Google button
  // registers it once, and a new one would reload the button. Updated in a
  // layout effect, before the browser paints the page without the box.
  const offered = React.useRef(staySignedInOffered)
  React.useLayoutEffect(() => {
    offered.current = staySignedInOffered
  }, [staySignedInOffered])
  const { getValues } = form
  const readStaySignedIn = React.useCallback(
    () => offered.current === true && getValues("staySignedIn") === true,
    [getValues]
  )
  // OI-280 PR B (T-B14): a window the passkey screen opened by itself carries the
  // box as it stood when it opened, the choice made on the first screen; a window
  // the button opened reads it when its answer comes back, as before. Taken once.
  const staySignedInAtOpen = React.useRef<boolean | null>(null)
  const takeStaySignedIn = React.useCallback(() => {
    const atOpen = staySignedInAtOpen.current
    staySignedInAtOpen.current = null
    return atOpen ?? readStaySignedIn()
  }, [readStaySignedIn])

  // Pending deletion (only surfaced on VALID credentials): route to the
  // recovery screen instead of a dead-end error. The server's localized message
  // carries the deletion deadline. True when the person was sent there.
  const toRecovery = (error: unknown, email?: string): boolean => {
    if (!recoveryPath || !getErrorCodes(error).includes("User.AccountPendingDeletion")) {
      return false
    }
    if (isAbsoluteUrl(recoveryPath)) {
      // Another origin owns the recovery screen; the email prefill cannot
      // ride along, and the screen asks for it anyway.
      window.location.assign(recoveryPath)
      return true
    }
    navigate(recoveryPath, {
      state: { email, message: getErrorMessage(error) },
    })
    return true
  }

  // S20: the passkey proves two factors, so no second step follows. It writes no
  // method and leaves no proof (T-C6); a passkey of another account signs that
  // account in. Everything but the verify is the ceremony's (usePasskeySignIn).
  const signInWithPasskey = async (
    credential: AuthenticationResponseJSON
  ): Promise<PasskeyOutcome> => {
    try {
      const result = await loginWithPasskey(credential, takeStaySignedIn())
      if (result.status === "twoFactorRequired") {
        challenge(result.challengeToken, result.methods)
        return { leaving: true }
      }
      passkeySignInEnded()
      toast.success(t("auth.welcomeBack"))
      // The person holds a passkey: no offer to add one follows (OI-280 PR B).
      complete(result, { signedInWithPasskey: true })
      return { leaving: true }
    } catch (error) {
      if (toRecovery(error)) return { leaving: true }
      return { message: passkeyFailureMessage(error) }
    }
  }
  // Under these the server already said passkeys run here: a first failure is said.
  const passkeyExpected = passkeyAsked || reason?.reason === "mfa_passkey"
  const passkey = usePasskeySignIn({
    suggestion: true,
    passkeysExpected: passkeyExpected,
    onCredential: signInWithPasskey,
  })
  const passkeyShown = passkey.phase !== "unavailable" && passkey.phase !== "checking"
  const passkeyLeads = passkeyShown && (passkeyExpected || passkeyNotice !== null)
  // OI-280 (T-A4, T-A5, T-A13): whether passkeys may run here. The deployment's
  // hint, read with the method from the same branding answer so the two change
  // together; a browser that can use a passkey; and no answer since that said
  // they do not run. Otherwise the page is as before: no lookup, and in password
  // mode the address and the password on one screen. A cached hint that is wrong
  // (passkeys switched off since) is put right by the options' 404, one round
  // trip after the page loads: the password field then joins the address
  // (T-A13), unless Continue came first, whose lookup then answers 404 (D6).
  const passkeysMayRun =
    options?.passkeySignInOffered === true &&
    passkey.phase !== "unavailable" &&
    !lookupOff
  // OI-280 PR B: what this page learned, that passkeys do not run here (the
  // options' or the lookup's 404) or that this browser cannot use one, is kept
  // for the page's life: no offer to add a passkey follows a sign-in from here.
  const passkeysOffHere = passkey.phase === "unavailable" || lookupOff
  React.useEffect(() => {
    if (passkeysOffHere) notePasskeysOff()
  }, [passkeysOffHere])
  // Password mode while passkeys may run: the address first, the password next.
  const addressFirst =
    step.kind === "credentials" && passkeysMayRun && method === "password"
  const showPassword =
    step.kind === "password" ||
    (step.kind === "credentials" && method === "password" && !passkeysMayRun)
  const codeFirst = step.kind === "credentials" && method === "code"
  // A door answered that the account signs in with its passkey: passkeys run
  // here, so the button is asked for at once, and a failure is said.
  const { confirm: confirmPasskeys } = passkey
  const askForPasskey = React.useCallback(
    (notice: string) => {
      setPasskeyNotice(notice)
      confirmPasskeys()
    },
    [confirmPasskeys]
  )

  // The sentence asks for the passkey: its button takes the focus once it can be
  // pressed (a disabled button cannot hold it, so a button still on its way gets
  // it when its options arrive). The passkey screen's button likewise (T-A7).
  const passkeyReady = passkey.phase === "ready"
  React.useEffect(() => {
    if (passkeyNotice && passkeyReady) passkeyButtonRef.current?.focus()
  }, [passkeyNotice, passkeyReady])
  const onPasskeyScreen = step.kind === "passkey"
  React.useEffect(() => {
    if (onPasskeyScreen && passkeyReady) passkeyScreenButtonRef.current?.focus()
  }, [onPasskeyScreen, passkeyReady])
  // OI-280 PR B (T-B14, owner Q4 (b), reversing D8): each arrival on the passkey
  // screen opens the browser's window by itself, once, as soon as the options
  // are in hand. Tried, never detected by the browser's name: a browser that
  // grants the window only to a click (WebKit, T-C2) answers NotAllowedError,
  // which the ceremony takes as a cancel, so the screen stays as it is, its
  // button focused and nothing said. Never twice for one arrival, and not at all
  // once "Try another way" is open, while the cookies note shows, or when
  // passkeys are gone; the first screen's passkey-first entries never reach
  // here. The button stays the way to try again, inside its own click.
  const { start: startPasskey } = passkey
  const passkeyGoneHere = passkey.phase === "unavailable"
  const openedForArrival = React.useRef(0)
  React.useEffect(() => {
    if (!onPasskeyScreen || passkeyArrival === 0) {
      // Off the screen, a window opened from anywhere else reads the box anew.
      staySignedInAtOpen.current = null
      return
    }
    if (openedForArrival.current === passkeyArrival) {
      // That window closed without an answer: the button reads the box anew.
      if (passkeyReady) staySignedInAtOpen.current = null
      return
    }
    if (otherWaysOpen || cookieBlocked || passkeyGoneHere) {
      openedForArrival.current = passkeyArrival
      return
    }
    if (!passkeyReady) return
    openedForArrival.current = passkeyArrival
    staySignedInAtOpen.current = readStaySignedIn()
    startPasskey()
  }, [
    onPasskeyScreen,
    passkeyArrival,
    otherWaysOpen,
    cookieBlocked,
    passkeyGoneHere,
    passkeyReady,
    readStaySignedIn,
    startPasskey,
  ])
  // A provider in that screen's list answered that the account signs in with
  // its passkey: the sentence shows above the button, which takes the focus back.
  React.useEffect(() => {
    if (onPasskeyScreen && passkeyNotice && passkeyReady) {
      passkeyScreenButtonRef.current?.focus()
    }
  }, [onPasskeyScreen, passkeyNotice, passkeyReady])
  // Under `mfa_passkey` the passkey screen's list holds the way out alone
  // (D-152-2). That link drops the request where the person stands: the same
  // page, with no reason left, so the list becomes everyone's list and the
  // focused link is gone. The focus moves to the list's first way, which a
  // screen reader then reads out.
  const lostPasskeyOnly = reason?.reason === "mfa_passkey"
  const otherWaysListRef = React.useRef<HTMLDivElement>(null)
  const lostPasskeyOnlyBeforeRef = React.useRef(lostPasskeyOnly)
  React.useEffect(() => {
    if (lostPasskeyOnlyBeforeRef.current && !lostPasskeyOnly && onPasskeyScreen) {
      otherWaysListRef.current?.querySelector<HTMLElement>("button")?.focus()
    }
    lostPasskeyOnlyBeforeRef.current = lostPasskeyOnly
  }, [lostPasskeyOnly, onPasskeyScreen])

  // Back to the address, keeping what was typed; the sent code is forgotten.
  const backToAddress = React.useCallback(() => {
    form.setValue("password", "")
    setStep({ kind: "credentials" })
  }, [form])

  // A sign-in by code that needs the password: the address is the one the code
  // just proved, and only the password is asked.
  const continueWithPassword = React.useCallback(
    (email: string) => {
      form.setValue("email", email)
      form.setValue("password", "")
      setStep({ kind: "password", email, afterCode: true })
    },
    [form]
  )

  const signInWithPassword = async (values: z.infer<typeof schema>) => {
    try {
      const result = await login(
        values.email,
        values.password,
        // A box not shown sends false. This handler is made afresh on every
        // render, so it reads the current answer; the ref is only for the
        // provider buttons' reader, which must stay one function.
        staySignedInOffered === true && values.staySignedIn
      )
      if (result.status === "twoFactorRequired") {
        rememberMethodForChallenge(result.challengeToken, "password")
        challenge(result.challengeToken, result.methods)
        return
      }
      signInEnded("password")
      toast.success(t("auth.welcomeBack"))
      complete(result, { proof: "password" })
    } catch (error) {
      // Unconfirmed email: send the user to enter the verification code, which
      // confirms the address and signs them in — no dead-end and no second
      // manual login. The password was already accepted, so it isn't needed.
      if (getErrorCodes(error).includes("User.EmailNotConfirmed")) {
        interstitial("/verify-email", { email: values.email })
        return
      }
      if (toRecovery(error, values.email)) return
      // The account signs in with its passkey alone (D-S20-4 (ii-s)). From the
      // password step, back to the address, where the passkey then leads.
      const notice = passkeyRequiredNotice(error)
      if (notice) {
        if (step.kind === "password") backToAddress()
        askForPasskey(notice)
        return
      }
      // One text for every wrong pair (OI-215): an unknown address, a wrong
      // password, and an account that has no password all read the same, and
      // the last of them is told where its way in is.
      if (
        codeOffered &&
        getErrorCodes(error).includes("User.InvalidCredentials")
      ) {
        toast.error(t("auth.invalidCredentialsOrCode"))
        return
      }
      toast.error(getErrorMessage(error))
    }
  }

  const sendCode = async (values: z.infer<typeof schema>) => {
    try {
      const pending = await startEmailCode(values.email, i18n.language)
      setStep({ kind: "code", pending })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  // The password step of an address-first sign-in (OI-280): the address the
  // person typed, and only the password asked.
  const toPasswordStep = (email: string) => {
    form.setValue("password", "")
    setStep({ kind: "password", email, afterCode: false })
  }

  // OI-280 (D-S20-10): after the address and "Continue", the one question. Its
  // answer decides the next step and nothing else: `passkey`, the passkey
  // screen; anything else, the next step as before — the code, or the password
  // step — and nothing said. No code is sent before the answer.
  const continueAfterAddress = async (values: z.infer<typeof schema>) => {
    // The method "Continue" was pressed in.
    const pressedIn = method
    const press = new AbortController()
    setPendingLookup(press)
    // Never rejects: every failure is the answer `unknown`.
    const answer: PasskeyLookup = await lookUpPasskey(values.email)
    // The answer is acted on only where the press left the page (REVIEW 159
    // F2). Given up meanwhile: the page is gone (the sign-in ended another
    // way), the step changed, or the person took the other method.
    const dropped = press.signal.aborted
    setPendingLookup(null)
    // Dropped, and nothing sent; so too when the person changed the address,
    // whose answer this is not: "Continue" asks again.
    if (dropped || form.getValues("email") !== values.email) return
    if (answer === "passkey") {
      // The server said passkeys run here, for this address.
      confirmPasskeys()
      setOtherWaysOpen(false)
      setPasskeyArrival((arrival) => arrival + 1)
      setStep({ kind: "passkey", email: values.email })
      return
    }
    if (answer === "off") setLookupOff(true)
    if (pressedIn === "code") return sendCode(values)
    toPasswordStep(values.email)
  }

  const onSubmit = async (values: z.infer<typeof schema>) => {
    setPasskeyNotice(null)
    if (showPassword) {
      if (values.password.length === 0) {
        form.setError("password", { message: t("validation.required") })
        return
      }
      return signInWithPassword(values)
    }
    if (passkeysMayRun) return continueAfterAddress(values)
    return sendCode(values)
  }

  // The list's code item (T-A8): the code for the address on the screen, as
  // "Continue" in code mode sends it. Latched in the click: one email per press.
  const sendCodeFromList = async (email: string) => {
    if (sendingFromListRef.current) return
    sendingFromListRef.current = true
    setSendingFromList(true)
    try {
      setChosen("code")
      await sendCode({ ...getValues(), email })
    } finally {
      sendingFromListRef.current = false
      setSendingFromList(false)
    }
  }

  if (skipSignIn) return <FullScreenLoader />

  if (step.kind === "code") {
    return (
      <React.Suspense
        fallback={
          <AuthLayout title={t("auth.emailCodeTitle")} {...appHeader}>
            <div className="flex justify-center">
              <Spinner className="text-muted-foreground" />
            </div>
          </AuthLayout>
        }
      >
        <EmailCodeFlow
          pending={step.pending}
          staySignedIn={
            staySignedInOffered === true && getValues("staySignedIn") === true
          }
          requireFullProof={fullProof}
          pageFooter={pageFooter}
          onChangeAddress={backToAddress}
          onPasswordRequired={continueWithPassword}
          onPasskeyRequired={(notice) => {
            backToAddress()
            askForPasskey(notice)
          }}
        />
      </React.Suspense>
    )
  }

  // The address as the steps after it show it: read-only, with the way back to
  // it. `username` keeps it paired with the password for password managers.
  const addressField = (
    <FormField
      control={form.control}
      name="email"
      render={({ field }) => (
        <FormItem>
          <FormLabel>{t("auth.email")}</FormLabel>
          <FormControl>
            <Input
              type="email"
              // The passkey suggestion (conditional UI, T-C5): only where
              // the browser offers it and passkeys run here.
              autoComplete={
                passkey.suggestionSupported ? "username webauthn" : "username"
              }
              autoFocus={step.kind === "credentials"}
              readOnly={step.kind !== "credentials"}
              placeholder="name@example.com"
              dir="ltr"
              {...field}
            />
          </FormControl>
          {step.kind !== "credentials" ? (
            <FormDescription>
              <Button
                type="button"
                variant="link"
                className="h-auto p-0 text-xs text-muted-foreground"
                onClick={backToAddress}
              >
                {t("auth.useDifferentEmail")}
              </Button>
            </FormDescription>
          ) : null}
          <FormMessage />
        </FormItem>
      )}
    />
  )

  const staySignedInField = staySignedInOffered ? (
    <FormField
      control={form.control}
      name="staySignedIn"
      render={({ field }) => (
        <FormItem orientation="horizontal">
          <FormControl>
            <Checkbox
              name={field.name}
              checked={field.value}
              onCheckedChange={(checked) => field.onChange(checked === true)}
            />
          </FormControl>
          <FieldContent>
            <FormLabel>{t("auth.staySignedIn")}</FormLabel>
          </FieldContent>
        </FormItem>
      )}
    />
  ) : staySignedInOffered === null ? (
    // Unknown until the branding answers: hold the box row's place, so
    // the box, when offered, does not push the button down as a click
    // lands on it. Nothing here is a control or a label.
    <Field
      orientation="horizontal"
      aria-hidden
      className="invisible"
      data-testid="stay-signed-in-placeholder"
    >
      <span className="size-4 shrink-0" />
      <FieldContent>
        <FieldTitle>{t("auth.staySignedIn")}</FieldTitle>
      </FieldContent>
    </Field>
  ) : null

  // The providers, which read the box when they sign in and hand a door's
  // "this account signs in with its passkey" to the page.
  const providerButtons = (
    <StaySignedInProvider value={readStaySignedIn}>
      <PasskeyRequiredProvider onPasskeyRequired={askForPasskey}>
        {providers}
      </PasskeyRequiredProvider>
    </StaySignedInProvider>
  )

  // The last session ended because the browser withheld the sign-in cookie: said
  // on the first screen and on the passkey screen, whose sign-in needs the same
  // cookie (REVIEW 159 F5).
  const cookiesBlockedNote = cookieBlocked ? (
    <Alert data-testid="cookies-blocked-note">
      <Info />
      <AlertDescription>{t("auth.cookiesBlockedNote")}</AlertDescription>
    </Alert>
  ) : null

  if (step.kind === "passkey") {
    // OI-280 (T-A7, T-A8): the passkey screen. The button uses the options the
    // page fetched on load, and the ceremony starts inside its click (T-C2); the
    // window also opens by itself once per arrival where the browser allows it
    // (PR B, T-B14), and nothing is sent or written before the person chooses.
    // Passkeys switched off since the answer: the button renders nothing, and
    // the list is shown without its link, so the screen is never empty (T-A10).
    const passkeyGone = passkey.phase === "unavailable"
    // Under `mfa_passkey` (`lostPasskeyOnly`) every other way would resume the
    // request into the application's refusal (D-152-2): the list holds the way
    // out alone.
    const email = step.email
    return (
      <AuthLayout
        title={t("auth.passkey.screenTitle")}
        subtitle={t("auth.passkey.screenSubtitle")}
        pageFooter={pageFooter}
        {...appHeader}
      >
        <Form {...form}>
          <FieldGroup>
            {cookiesBlockedNote}
            {addressField}
            {staySignedInField}
            {/* A provider in the list answered that this account signs in with
                its passkey: the API's sentence stays on the screen, above the
                button, as on the first screen. */}
            {passkeyNotice ? (
              <Alert data-testid="passkey-required-note">
                <KeyRound />
                <AlertDescription>{passkeyNotice}</AlertDescription>
              </Alert>
            ) : null}
            <PasskeyButton
              ceremony={passkey}
              label={t("auth.passkey.signIn")}
              variant="default"
              buttonRef={passkeyScreenButtonRef}
            />
            {/* A disclosure: the link shows the list in place (D-S20-16). Without
                the button, the list is all the screen offers, so it is shown. */}
            {passkeyGone ? null : (
              <Button
                type="button"
                variant="link"
                className="w-full text-muted-foreground"
                aria-expanded={otherWaysOpen}
                aria-controls={OTHER_WAYS_ID}
                data-testid="try-another-way"
                onClick={() => setOtherWaysOpen((open) => !open)}
              >
                {t("auth.passkey.tryAnotherWay")}
              </Button>
            )}
            {otherWaysOpen || passkeyGone ? (
              <FieldSet id={OTHER_WAYS_ID} data-testid="other-ways">
                <FieldLegend variant="label">
                  {t("auth.passkey.otherWaysHeading")}
                </FieldLegend>
                {lostPasskeyOnly ? (
                  // D-152-2's link, as in the reason's sentence: it drops the
                  // request, and the sign-in then lands on Profile › Security.
                  <Button asChild variant="outline" className="w-full">
                    <Link
                      to="/login"
                      replace
                      state={{ from: LOST_PASSKEY_DESTINATION }}
                      data-testid="passkey-lost-link"
                    >
                      {t("auth.passkey.lostIt")}
                    </Link>
                  </Button>
                ) : (
                  // The same list for every account (D-S20-16): the
                  // deployment's options decide it, never the account.
                  <div ref={otherWaysListRef} className="flex flex-col gap-3">
                    {codeOffered ? (
                      <Button
                        type="button"
                        variant="outline"
                        className="w-full"
                        disabled={sendingFromList}
                        onClick={() => void sendCodeFromList(email)}
                      >
                        {sendingFromList ? (
                          <Spinner data-icon="inline-start" />
                        ) : null}
                        {sendingFromList
                          ? t("auth.sendingCode")
                          : t("auth.signInWithEmailCode")}
                      </Button>
                    ) : null}
                    <Button
                      type="button"
                      variant="outline"
                      className="w-full"
                      onClick={() => {
                        setChosen("password")
                        toPasswordStep(email)
                      }}
                    >
                      {t("auth.signInWithPasswordInstead")}
                    </Button>
                    {providerButtons}
                  </div>
                )}
              </FieldSet>
            ) : null}
          </FieldGroup>
        </Form>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title={
        passwordToFinish
          ? t("auth.passwordToFinishTitle")
          : codeFirst && options?.selfRegistrationOpen
            ? t("auth.signInOrCreateTitle")
            : t("auth.signInTitle")
      }
      subtitle={
        passwordToFinish
          ? t("auth.passwordToFinishSubtitle")
          : appBranding
            ? // The layout names the application under the title itself.
              undefined
            : (subtitle ?? t("auth.signInSubtitle"))
      }
      footer={codeFirst ? undefined : footer}
      pageFooter={pageFooter}
      {...appHeader}
    >
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
            {reason ? (
              // Why the application sent the person back (S23). The words
              // say what to do next; the server decides again on the resume.
              <Alert data-testid="sign-in-reason">
                <ShieldCheck />
                <AlertTitle>{t(REASON_TEXT[reason.reason].title)}</AlertTitle>
                <AlertDescription>
                  {t(REASON_TEXT[reason.reason].body)}
                  {/* The way out that sentence names (D-152-2): a sign-in here would
                      resume the request, and the server would end it at the
                      application, so the person would never see Profile. The link
                      drops the request; the guard and the completion both read `from`. */}
                  {reason.reason === "mfa_passkey" ? (
                    <Link
                      to="/login"
                      replace
                      state={{ from: LOST_PASSKEY_DESTINATION }}
                      className="underline underline-offset-4"
                      data-testid="passkey-lost-link"
                    >
                      {t("auth.passkey.lostIt")}
                    </Link>
                  ) : null}
                </AlertDescription>
              </Alert>
            ) : null}
            {cookiesBlockedNote}
            {passkeyNotice ? (
              <Alert data-testid="passkey-required-note">
                <KeyRound />
                <AlertDescription>{passkeyNotice}</AlertDescription>
              </Alert>
            ) : null}
            {/* The three passkey-first entries (T-A6): the "sign in again" of a
                passkey account, `mfa_passkey`, and a door's notice. Each comes
                from the person's own session or the server, never from a typed
                address. There is no passkey button for every visitor (D-S20-11). */}
            {passkeyLeads && step.kind === "credentials" ? (
              <PasskeyButton
                ceremony={passkey}
                label={t("auth.passkey.signIn")}
                variant="default"
                buttonRef={passkeyButtonRef}
              />
            ) : null}
            {addressField}
            {showPassword ? (
              <FormField
                control={form.control}
                name="password"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex items-center justify-between">
                      <FormLabel>{t("auth.password")}</FormLabel>
                      {/* The pending authorize request rides along, so the
                          request page keeps the application's header, and so
                          does its sign-in reason; nothing else of the query
                          string does. */}
                      <Link
                        to={{
                          pathname: "/forgot-password",
                          search: toAccountsSearch(returnTo, reason),
                        }}
                        className="text-xs text-muted-foreground underline-offset-4 hover:underline"
                      >
                        {t("auth.forgotPassword")}
                      </Link>
                    </div>
                    <FormControl>
                      <Input
                        type="password"
                        autoComplete="current-password"
                        autoFocus={step.kind === "password"}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            ) : null}
            {passwordToFinish ? null : staySignedInField}
            <Button
              type="submit"
              // The password step has no passkey above it: its button is the page's.
              variant={passkeyLeads && !passwordStep ? "secondary" : "default"}
              className="w-full"
              disabled={form.formState.isSubmitting}
            >
              {form.formState.isSubmitting ? (
                <>
                  <Spinner />
                  {showPassword
                    ? t("auth.signingIn")
                    : addressFirst
                      ? t("auth.continue")
                      : lookingUp
                        ? // Code mode while the question after the address is
                          // on its way: no code yet (REVIEW 159 F1).
                          t("auth.continueWithEmail")
                        : t("auth.sendingCode")}
                </>
              ) : showPassword ? (
                t("auth.signIn")
              ) : addressFirst ? (
                t("auth.continue")
              ) : (
                t("auth.continueWithEmail")
              )}
            </Button>
            {/* The other method, always a visible link (D-215-2). From the
                password step it sends the code for the address on that step,
                as the passkey screen's list does: the address is not asked
                about again. */}
            {codeOffered && !passwordToFinish ? (
              <Button
                type="button"
                variant="link"
                className="text-muted-foreground"
                disabled={passwordStep && sendingFromList}
                onClick={() => {
                  form.clearErrors()
                  if (step.kind === "password") {
                    void sendCodeFromList(step.email)
                    return
                  }
                  setChosen(codeFirst ? "password" : "code")
                }}
              >
                {codeFirst
                  ? t("auth.signInWithPasswordInstead")
                  : t("auth.signInWithEmailCode")}
              </Button>
            ) : null}
          </FieldGroup>
        </form>
      </Form>
      {step.kind === "credentials" ? providerButtons : null}
    </AuthLayout>
  )
}
