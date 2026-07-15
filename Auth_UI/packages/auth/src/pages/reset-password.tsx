import { zodResolver } from "@hookform/resolvers/zod"
import { Loader2 } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@astoom/api/client"
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
import { getErrorMessage } from "@astoom/api/errors"
import { AuthLayout } from "@astoom/ui/auth-layout"

/**
 * Sets a new password from a reset link. The token in the query string is the
 * whole credential - it identifies the user server-side, so nothing else is
 * asked for. It is captured once on mount and then stripped from the URL, which
 * means a refresh drops it and lands on the invalid-link state by design.
 */
export function ResetPasswordPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const [token] = React.useState(() => searchParams.get("token") ?? "")

  React.useEffect(() => {
    if (!token) return
    // Keep the token out of the address bar, browser history and any Referer.
    window.history.replaceState(window.history.state, "", window.location.pathname)
  }, [token])

  const schema = z
    .object({
      newPassword: z.string().min(8, t("validation.minLength", { count: 8 })),
      confirmNewPassword: z.string().min(1, t("validation.required")),
    })
    .refine((data) => data.newPassword === data.confirmNewPassword, {
      message: t("validation.passwordMismatch"),
      path: ["confirmNewPassword"],
    })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { newPassword: "", confirmNewPassword: "" },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const { error } = await api.POST("/api/v1/Auth/reset-password", {
        body: { token, ...values },
      })
      if (error) throw error
      toast.success(t("auth.resetSuccess"))
      navigate("/login", { replace: true })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  if (!token) {
    return (
      <AuthLayout
        title={t("auth.resetLinkInvalidTitle")}
        subtitle={t("auth.resetLinkInvalidDescription")}
        footer={
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t("auth.backToSignIn")}
          </Link>
        }
      >
        <Button asChild className="w-full">
          <Link to="/forgot-password">{t("auth.requestNewResetLink")}</Link>
        </Button>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title={t("auth.resetTitle")}
      subtitle={t("auth.resetSubtitle")}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
            <FormField
              control={form.control}
              name="newPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.newPassword")}</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      autoComplete="new-password"
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
              name="confirmNewPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.confirmPassword")}</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      autoComplete="new-password"
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
                <Loader2 className="animate-spin" />
              ) : null}
              {t("auth.resetPassword")}
            </Button>
          </FieldGroup>
        </form>
      </Form>
    </AuthLayout>
  )
}
