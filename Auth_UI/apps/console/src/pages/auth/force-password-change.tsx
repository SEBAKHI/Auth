import { zodResolver } from "@hookform/resolvers/zod"
import { Loader2 } from "lucide-react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
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
import { AuthLayout } from "./auth-layout"

export function ForcePasswordChangePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const schema = z
    .object({
      currentPassword: z.string().min(1, t("validation.required")),
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
      currentPassword: "",
      newPassword: "",
      confirmNewPassword: "",
    },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const { error } = await api.POST("/api/v1/Auth/change-password", {
        body: { ...values, terminateSessions: false },
      })
      if (error) throw error
      toast.success(t("profile.passwordChanged"))
      navigate("/", { replace: true })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout title={t("auth.forceTitle")} subtitle={t("auth.forceSubtitle")}>
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
            <FormField
              control={form.control}
              name="currentPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.currentPassword")}</FormLabel>
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
              {t("auth.changePassword")}
            </Button>
          </FieldGroup>
        </form>
      </Form>
    </AuthLayout>
  )
}
