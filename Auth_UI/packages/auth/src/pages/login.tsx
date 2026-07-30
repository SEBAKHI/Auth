import { zodResolver } from "@hookform/resolvers/zod"
import type * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { Button } from "@astoom/ui/button"
import { FieldGroup } from "@astoom/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { useAuth } from "@astoom/auth/auth-context"
import { getErrorCodes, getErrorMessage } from "@astoom/api/errors"
import { AuthLayout } from "@astoom/ui/auth-layout"
import { useBranding } from "@astoom/ui/branding"

import { getReturnToClientId, getValidReturnTo } from "../return-to"
import { useAppBranding } from "../use-app-branding"
import { Spinner } from "@astoom/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface LocationState {
  from?: { pathname?: string; search?: string }
  email?: string
}

export function LoginPage({
  providers,
  footer,
  pageFooter,
  subtitle,
}: {
  /** External sign-in options rendered under the credentials form. */
  providers?: React.ReactNode
  /** Extra content under the card (e.g. a create-account link). */
  footer?: React.ReactNode
  /** Ambient links pinned to the bottom of the page (e.g. privacy policy). */
  pageFooter?: React.ReactNode
  /** Overrides the console-flavored default subtitle. */
  subtitle?: string
} = {}) {
  const { t } = useTranslation()
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null
  const from = state?.from?.pathname
    ? state.from.pathname + (state.from.search ?? "")
    : "/"
  const presetEmail = state?.email ?? ""

  // Pending OAuth authorize request (hosted-login flow): strictly validated —
  // only the auth origin's authorize endpoint is ever a legal destination.
  const returnTo = getValidReturnTo(location.search)
  const appBranding = useAppBranding(getReturnToClientId(returnTo))
  const { name: platformName } = useBranding()

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
        navigate("/two-factor", {
          replace: true,
          state: { challengeToken: result.challengeToken, from, returnTo },
        })
        return
      }
      toast.success(t("auth.welcomeBack"))
      if (result.requiresPasswordChange) {
        navigate("/force-password-change", { replace: true })
      } else if (returnTo) {
        // Resume the pending authorize request: a top-level navigation so the
        // freshly set IdP session cookie rides along and the code is issued.
        window.location.assign(returnTo)
      } else {
        navigate(from, { replace: true })
      }
    } catch (error) {
      // Unconfirmed email: send the user to enter the verification code, which
      // confirms the address and signs them in — no dead-end and no second
      // manual login. The password was already accepted, so it isn't needed.
      if (getErrorCodes(error).includes("User.EmailNotConfirmed")) {
        navigate("/verify-email", {
          state: { email: values.email, from },
        })
        return
      }
      // Pending deletion (only surfaced on VALID credentials): route to the
      // recovery screen instead of a dead-end error. The server's localized
      // message carries the deletion deadline.
      if (getErrorCodes(error).includes("User.AccountPendingDeletion")) {
        navigate("/account-recovery", {
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
      appName={appBranding?.name}
      appLogoUrl={appBranding?.logoUrl}
      securedBy={t("auth.securedBy", { name: platformName })}
    >
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
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
                    <Link
                      to="/forgot-password"
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
