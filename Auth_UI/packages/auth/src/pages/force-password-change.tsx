import { zodResolver } from "@hookform/resolvers/zod"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@authsystem/api/client"
import { PASSWORD_LENGTH_FLOOR } from "@authsystem/api/constants"
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
import { getErrorMessage } from "@authsystem/api/errors"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Spinner } from "@authsystem/ui/spinner"

export function ForcePasswordChangePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const schema = z
    .object({
      currentPassword: z.string().min(1, t("validation.required")),
      newPassword: z
        .string()
        .min(PASSWORD_LENGTH_FLOOR, t("validation.minLength", { count: PASSWORD_LENGTH_FLOOR })),
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
                <Spinner />
              ) : null}
              {t("auth.changePassword")}
            </Button>
          </FieldGroup>
        </form>
      </Form>
    </AuthLayout>
  )
}
