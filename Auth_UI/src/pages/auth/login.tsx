import { zodResolver } from "@hookform/resolvers/zod"
import { Loader2 } from "lucide-react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

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
import { useAuth } from "@/lib/auth/auth-context"
import { getErrorMessage } from "@/lib/errors"
import { AuthLayout } from "./auth-layout"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface LocationState {
  from?: { pathname?: string; search?: string }
  email?: string
}

export function LoginPage() {
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
      toast.success(t("auth.welcomeBack"))
      if (result.requiresPasswordChange) {
        navigate("/force-password-change", { replace: true })
      } else if (result.requiresTwoFactor) {
        navigate("/two-factor", { replace: true })
      } else {
        navigate(from, { replace: true })
      }
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout
      title={t("auth.signInTitle")}
      subtitle={t("auth.signInSubtitle")}
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
    </AuthLayout>
  )
}
