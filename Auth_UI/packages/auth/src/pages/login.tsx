import { zodResolver } from "@hookform/resolvers/zod"
import { Loader2 } from "lucide-react"
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

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface LocationState {
  from?: { pathname?: string; search?: string }
  email?: string
}

export function LoginPage({
  providers,
  footer,
  subtitle,
}: {
  /** External sign-in options rendered under the credentials form. */
  providers?: React.ReactNode
  /** Extra content under the card (e.g. a create-account link). */
  footer?: React.ReactNode
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
          state: { challengeToken: result.challengeToken, from },
        })
        return
      }
      toast.success(t("auth.welcomeBack"))
      if (result.requiresPasswordChange) {
        navigate("/force-password-change", { replace: true })
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
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout
      title={t("auth.signInTitle")}
      subtitle={subtitle ?? t("auth.signInSubtitle")}
      footer={footer}
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
                  <Loader2 className="animate-spin" />
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
