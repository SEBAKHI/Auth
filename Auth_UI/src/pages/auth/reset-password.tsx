import { zodResolver } from "@hookform/resolvers/zod"
import { Loader2 } from "lucide-react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@/lib/api/client"
import { Button } from "@/components/ui/button"
import { FieldGroup } from "@/components/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { getErrorMessage } from "@/lib/errors"
import { AuthLayout } from "./auth-layout"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function ResetPasswordPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()
  const presetEmail = (location.state as { email?: string } | null)?.email ?? ""

  const schema = z
    .object({
      email: z
        .string()
        .min(1, t("validation.required"))
        .regex(EMAIL_RE, t("validation.email")),
      token: z.string().min(1, t("validation.required")),
      newPassword: z.string().min(8, t("validation.minLength", { count: 8 })),
      confirmNewPassword: z.string().min(1, t("validation.required")),
    })
    .refine((data) => data.newPassword === data.confirmNewPassword, {
      message: t("validation.passwordMismatch"),
      path: ["confirmNewPassword"],
    })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: presetEmail,
      token: "",
      newPassword: "",
      confirmNewPassword: "",
    },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const { error } = await api.POST("/api/v1/Auth/reset-password", {
        body: values,
      })
      if (error) throw error
      toast.success(t("auth.resetSuccess"))
      navigate("/login", { replace: true })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
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
              name="email"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.email")}</FormLabel>
                  <FormControl>
                    <Input type="email" autoComplete="username" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="token"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.resetCode")}</FormLabel>
                  <FormControl>
                    <Input autoComplete="one-time-code" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
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
