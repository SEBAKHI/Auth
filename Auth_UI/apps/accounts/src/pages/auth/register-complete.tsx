import { zodResolver } from "@hookform/resolvers/zod"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import { useAuth } from "@authsystem/auth/auth-context"
import { useLoginCompletion } from "@authsystem/auth/login-completion"
import { PasswordField } from "@authsystem/auth/password-field"
import {
  applyPasswordServerErrors,
  passwordSchema,
} from "@authsystem/auth/password-rules"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
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
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import {
  clearRegistrationFlow,
  clearVerifiedCode,
  getVerifiedCode,
  readPendingRegistration,
} from "./registration-flow"

/**
 * The browser's zone, when it is one the server will accept. The validator
 * takes IANA identifiers only (a slash, or the literal UTC), and a refused zone
 * would fail the whole completion at validation, before the code is even read.
 * An odd runtime value therefore sends nothing, and the profile keeps its
 * "automatic" default.
 */
function detectedTimeZone(): string | undefined {
  try {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
    return zone && zone.includes("/") && zone.length <= 50 ? zone : undefined
  } catch {
    return undefined
  }
}

/**
 * Offers the new credential to the browser's password manager.
 *
 * The completion navigates away as a single-page transition, and not every
 * password manager notices a form that was never "submitted" to a new
 * document. The Credential Management API says it outright, where supported;
 * elsewhere the real, read-only username field beside the password field is
 * what the heuristics key on. Best effort by definition: a refusal here is
 * not a failed sign-up.
 */
async function offerCredential(email: string, password: string): Promise<void> {
  if (typeof window === "undefined" || !navigator.credentials?.store) return
  // Chromium-only and absent from the DOM typings, so it is looked up rather
  // than named: where the constructor is missing there is nothing to offer.
  const Ctor = (
    window as unknown as {
      PasswordCredential?: new (data: {
        id: string
        password: string
        name?: string
      }) => Credential
    }
  ).PasswordCredential
  if (!Ctor) return
  try {
    await navigator.credentials.store(
      new Ctor({ id: email, password, name: email })
    )
  } catch {
    // Declined, unsupported in this context, or a policy refusal: fine.
  }
}

/**
 * Third of the three sign-up screens: a name and a password for an address
 * the code has already proven.
 *
 * The request carries the pending handle and the code once more, never the
 * address — the server knows which address the handle belongs to, and a body
 * that named one would be a body a caller could name differently. The address
 * is nevertheless shown, real and read-only, in a field with the username
 * autocomplete role: that is the field password managers pair the new
 * password with, and a masked value there would save a credential under a
 * name that cannot sign in. Changing it means starting over, and the link
 * says so by going back to the first screen.
 *
 * Routed at the top level rather than under RequireAnonymous. The successful
 * request signs the person in, and an anonymous-only guard would race that
 * transition; the guard here is the page's own — a visitor who is already
 * signed in and has no pending authorize request is sent home, and one who
 * arrives without a verified code in memory (a reload, a typed URL) is sent
 * back to the code screen.
 */
export function RegisterCompletePage() {
  const { t } = useTranslation()
  const { status, completeRegistration } = useAuth()
  const navigate = useNavigate()
  const { search } = useLocation()
  const { returnTo, complete, challenge } = useLoginCompletion({
    defaultFrom: "/profile",
  })
  const { policy } = usePasswordPolicy()

  // Read once on mount: the identity is the tab's, the code is this document's.
  const [pending] = React.useState(readPendingRegistration)
  const [otp] = React.useState(getVerifiedCode)
  // "done" removes the form from the tree before anything else happens to the
  // values it held; "submitting" keeps the self-guard from bouncing the page
  // in the instant between the session being adopted and the navigation.
  const [phase, setPhase] = React.useState<"form" | "submitting" | "done">(
    "form"
  )

  const schema = z.object({
    firstName: z.string().trim().min(1, t("validation.required")),
    lastName: z.string().trim().min(1, t("validation.required")),
    password: passwordSchema(policy),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: "", lastName: "", password: "" },
  })

  if (!pending) {
    return <Navigate to={{ pathname: "/register", search }} replace />
  }
  if (!otp) {
    return <Navigate to={{ pathname: "/register/verify", search }} replace />
  }
  if (status === "authenticated" && phase === "form" && !returnTo) {
    return <Navigate to="/" replace />
  }

  const email = pending.email

  const onSubmit = async (values: z.infer<typeof schema>) => {
    setPhase("submitting")
    try {
      const result = await completeRegistration({
        pendingId: pending.pendingId,
        otp,
        password: values.password,
        firstName: values.firstName,
        lastName: values.lastName,
        timeZone: detectedTimeZone(),
      })
      setPhase("done")
      clearRegistrationFlow()
      if (result.status === "twoFactorRequired") {
        // A brand-new account has no second factor; handled all the same so
        // the shared tail never has a branch this screen cannot answer.
        challenge(result.challengeToken)
        return
      }
      await offerCredential(email, values.password)
      toast.success(t("auth.accountCreated"))
      complete(result)
    } catch (error) {
      setPhase("form")
      const codes = getErrorCodes(error)
      // The code was refused: rotated by a newer request, expired, or spent
      // by another completion. Nothing on this screen can fix that, so the
      // proof is dropped and the person retypes a code, with the reason shown
      // there rather than lost in a toast here.
      if (codes.some((code) => code.startsWith("EmailVerification."))) {
        clearVerifiedCode()
        navigate(
          { pathname: "/register/verify", search },
          { replace: true, state: { notice: getErrorMessage(error) } }
        )
        return
      }
      // The account exists: created by another door while the code was being
      // typed, created by this very form's earlier submission, or created but
      // not signed in. In every case the way forward is the ordinary sign-in.
      if (
        codes.includes("User.DuplicateEmail") ||
        codes.includes("User.AccountCreatedSignInRequired")
      ) {
        clearRegistrationFlow()
        toast.info(getErrorMessage(error))
        navigate(
          { pathname: "/login", search },
          { replace: true, state: { email } }
        )
        return
      }
      // A refused password lands under the field, every reason at once; only
      // a failure about something else is left to the toast.
      if (!applyPasswordServerErrors(form, "password", error)) {
        toast.error(getErrorMessage(error))
      }
    }
  }

  return (
    <AuthLayout
      title={t("auth.registerCompleteTitle")}
      subtitle={t("auth.registerCompleteSubtitle")}
    >
      {phase === "done" ? (
        <div className="flex justify-center">
          <Spinner className="text-muted-foreground" />
        </div>
      ) : (
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)}>
            <FieldGroup>
              <Field>
                <FieldLabel htmlFor="registration-email">
                  {t("auth.email")}
                </FieldLabel>
                <Input
                  id="registration-email"
                  type="email"
                  name="email"
                  autoComplete="username"
                  value={email}
                  readOnly
                  tabIndex={-1}
                  dir="ltr"
                />
                <FieldDescription>
                  <Link
                    to={{ pathname: "/register", search }}
                    onClick={clearRegistrationFlow}
                  >
                    {t("auth.changeEmail")}
                  </Link>
                </FieldDescription>
              </Field>
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
              <PasswordField
                control={form.control}
                name="password"
                label={t("auth.password")}
              />
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
      )}
    </AuthLayout>
  )
}
