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
import { FieldGroup } from "@authsystem/ui/field"
import {
  Form,
  FormControl,
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

import { isAbsoluteUrl } from "../external/recovery-navigation"
import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion } from "../login-completion"
import { Spinner } from "@authsystem/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface LocationState {
  email?: string
}

export function LoginPage({
  providers,
  footer,
  pageFooter,
  subtitle,
  recoveryPath,
}: {
  /** External sign-in options rendered under the credentials form. */
  providers?: React.ReactNode
  /** Extra content under the card (e.g. a create-account link). */
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
  const { t } = useTranslation()
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

  const schema = z.object({
    email: z
      .string()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
    password: z.string().min(1, t("validation.required")),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: presetEmail, password: "" },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const result = await login(values.email, values.password)
      if (result.status === "twoFactorRequired") {
        challenge(result.challengeToken)
        return
      }
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
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout
      title={t("auth.signInTitle")}
      subtitle={
        appBranding
          ? t("auth.continueToApp", { name: appBranding.name })
          : (subtitle ?? t("auth.signInSubtitle"))
      }
      footer={footer}
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
                      autoFocus
                      placeholder="name@example.com"
                      dir="ltr"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
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
                        search: returnTo
                          ? `?returnTo=${encodeURIComponent(returnTo)}`
                          : "",
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
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <Button
              type="submit"
              className="w-full"
              disabled={form.formState.isSubmitting}
            >
              {form.formState.isSubmitting ? (
                <>
                  <Spinner />
                  {t("auth.signingIn")}
                </>
              ) : (
                t("auth.signIn")
              )}
            </Button>
          </FieldGroup>
        </form>
      </Form>
      {providers}
    </AuthLayout>
  )
}
