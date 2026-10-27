import { zodResolver } from "@hookform/resolvers/zod"
import { Info } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { Checkbox } from "@authsystem/ui/checkbox"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
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
import { toAccountsSearch } from "../return-to"
import {
  isFullProofRequested,
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
 * code first; "code" is everything after a code was sent; "password" is the
 * password a sign-in by code still needs (the account holds platform authority,
 * or the "sign in again" dialog asked for full proof).
 */
type Step =
  | { kind: "credentials" }
  | { kind: "code"; pending: PendingEmailCode }
  | { kind: "password"; email: string }

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
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null
  const presetEmail = state?.email ?? ""

  // Pending OAuth authorize request (hosted-login flow): strictly validated —
  // only the auth origin's authorize endpoint is ever a legal destination.
  const { returnTo, complete, challenge, interstitial } = useLoginCompletion()
  // The last session ended at its first refresh because the browser withheld
  // the sign-in cookie from the API host. Without saying so, "you were signed
  // out" would repeat on every sign-in with nothing to act on.
  const [cookieBlocked] = React.useState(isCookieBlocked)
  const { appBranding, layout: appHeader } = useFlowBranding(returnTo)
  // Hidden when the deployment's administrator decides for everyone (OI-165).
  // The server applies that decision whatever this page sends.
  const { staySignedInOffered, signInOptions } = useBranding()
  // OI-215. Before the code, only what the visitor already knows decides the
  // page: the deployment's options, the method that last worked on this
  // browser, and their own click. Never anything about the address.
  const options = signInOptions ?? null
  const codeOffered = options?.codeSignInOffered === true
  const [remembered] = React.useState(readLastSignInMethod)
  const [chosen, setChosen] = React.useState<SignInMethod | null>(
    state?.method === "password" ? "password" : null
  )
  const method: SignInMethod =
    options && codeOffered
      ? (chosen ?? primarySignInMethod(options, remembered))
      : "password"
  const [fullProof] = React.useState(isFullProofRequested)
  const [step, setStep] = React.useState<Step>({ kind: "credentials" })

  const passwordToFinish = step.kind === "password"
  const showPassword = passwordToFinish || method === "password"
  const codeFirst = !passwordToFinish && method === "code"

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
      complete(result)
    } catch (error) {
      // Unconfirmed email: send the user to enter the verification code, which
      // confirms the address and signs them in — no dead-end and no second
      // manual login. The password was already accepted, so it isn't needed.
      if (getErrorCodes(error).includes("User.EmailNotConfirmed")) {
        interstitial("/verify-email", { email: values.email })
        return
      }
      // Pending deletion (only surfaced on VALID credentials): route to the
      // recovery screen instead of a dead-end error. The server's localized
      // message carries the deletion deadline.
      if (
        recoveryPath &&
        getErrorCodes(error).includes("User.AccountPendingDeletion")
      ) {
        if (isAbsoluteUrl(recoveryPath)) {
          // Another origin owns the recovery screen; the email prefill cannot
          // ride along, and the screen asks for it anyway.
          window.location.assign(recoveryPath)
          return
        }
        navigate(recoveryPath, {
          state: { email: values.email, message: getErrorMessage(error) },
        })
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

  const onSubmit = async (values: z.infer<typeof schema>) => {
    if (!showPassword) return sendCode(values)
    if (values.password.length === 0) {
      form.setError("password", { message: t("validation.required") })
      return
    }
    return signInWithPassword(values)
  }

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
      setStep({ kind: "password", email })
    },
    [form]
  )

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
        />
      </React.Suspense>
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
            ? t("auth.continueToApp", { name: appBranding.name })
            : (subtitle ?? t("auth.signInSubtitle"))
      }
      footer={codeFirst ? undefined : footer}
      pageFooter={pageFooter}
      {...appHeader}
    >
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
            {cookieBlocked ? (
              <Alert data-testid="cookies-blocked-note">
                <Info />
                <AlertDescription>{t("auth.cookiesBlockedNote")}</AlertDescription>
              </Alert>
            ) : null}
            <FormField
              control={form.control}
              name="email"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.email")}</FormLabel>
                  <FormControl>
                    <Input
                      type="email"
                      autoComplete="username"
                      autoFocus={!passwordToFinish}
                      readOnly={passwordToFinish}
                      placeholder="name@example.com"
                      dir="ltr"
                      {...field}
                    />
                  </FormControl>
                  {passwordToFinish ? (
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
            {showPassword ? (
              <FormField
                control={form.control}
                name="password"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex items-center justify-between">
                      <FormLabel>{t("auth.password")}</FormLabel>
                      {/* The pending authorize request rides along, so the
                          request page keeps the application's header; nothing
                          else of the query string does. */}
                      <Link
                        to={{
                          pathname: "/forgot-password",
                          search: toAccountsSearch(returnTo),
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
                        autoFocus={passwordToFinish}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            ) : null}
            {passwordToFinish ? null : staySignedInOffered ? (
              <FormField
                control={form.control}
                name="staySignedIn"
                render={({ field }) => (
                  <FormItem orientation="horizontal">
                    <FormControl>
                      <Checkbox
                        name={field.name}
                        checked={field.value}
                        onCheckedChange={(checked) =>
                          field.onChange(checked === true)
                        }
                      />
                    </FormControl>
                    <FieldContent>
                      <FormLabel>{t("auth.staySignedIn")}</FormLabel>
                      <FormDescription>
                        {t("auth.staySignedInHint")}
                      </FormDescription>
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
                  <FieldDescription>
                    {t("auth.staySignedInHint")}
                  </FieldDescription>
                </FieldContent>
              </Field>
            ) : null}
            <Button
              type="submit"
              className="w-full"
              disabled={form.formState.isSubmitting}
            >
              {form.formState.isSubmitting ? (
                <>
                  <Spinner />
                  {showPassword ? t("auth.signingIn") : t("auth.sendingCode")}
                </>
              ) : showPassword ? (
                t("auth.signIn")
              ) : (
                t("auth.continueWithEmail")
              )}
            </Button>
            {/* The other method, always a visible link (D-215-2). */}
            {codeOffered && !passwordToFinish ? (
              <Button
                type="button"
                variant="link"
                className="text-muted-foreground"
                onClick={() => {
                  form.clearErrors()
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
      {passwordToFinish ? null : (
        <StaySignedInProvider value={readStaySignedIn}>
          {providers}
        </StaySignedInProvider>
      )}
    </AuthLayout>
  )
}
