import { zodResolver } from "@hookform/resolvers/zod"
import { useQuery } from "@tanstack/react-query"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import { Button } from "@authsystem/ui/button"
import { FieldGroup } from "@authsystem/ui/field"
import { Skeleton } from "@authsystem/ui/skeleton"

import { useLoginCompletion } from "../login-completion"
import { PasswordField } from "../password-field"
import { applyPasswordServerErrors, passwordSchema } from "../password-rules"
import { SetPasswordPanel } from "../set-password-panel"
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

  /*
   * The form below demands a current password, so an account that has none - a
   * Google or Apple sign-up an administrator has flagged for a password change -
   * could only ever be told its current password was wrong, on the one page it
   * is not allowed to leave. `GET /Auth/me` is claims-only and carries no such
   * flag, so the fuller user record is what answers the question.
   */
  const meQuery = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })

  if (meQuery.isPending) {
    return (
      <AuthLayout title={t("auth.forceTitle")} subtitle={t("auth.forceSubtitle")}>
        <div className="flex flex-col gap-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </div>
      </AuthLayout>
    )
  }

  // Fails closed: only an explicit false swaps the form out. A failed or older
  // request keeps today's behaviour rather than blanking the page or offering a
  // link to an account that may well have a password.
  if (meQuery.data?.hasPassword === false && meQuery.data.email) {
    return (
      <AuthLayout title={t("profile.setPassword")} subtitle={t("auth.forceSubtitle")}>
        <SetPasswordPanel email={meQuery.data.email} />
      </AuthLayout>
    )
  }

  return <ForcePasswordChangeForm />
}

function ForcePasswordChangeForm() {
  const { t } = useTranslation()
  // This screen stands between a successful sign-in and the session the user
  // actually asked for, so it ends the authentication like any other screen
  // that can: a pending authorize request has to survive it, not die here.
  const { complete } = useLoginCompletion()
  const { policy } = usePasswordPolicy()

  const schema = z
    .object({
      currentPassword: z.string().min(1, t("validation.required")),
      newPassword: passwordSchema(policy),
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
        // terminateSessions is deliberately NOT sent. Omitting it leaves the
        // decision to the operator's Session:TerminateSessionsOnPasswordChange
        // setting, which defaults to true. Sending false overrode that switch
        // outright, so the console offered "sign out everywhere on password
        // change", showed it turned on, and every other browser stayed signed
        // in — the reset page never sent it, which is why only reset worked.
        body: values,
      })
      if (error) throw error
      toast.success(t("profile.passwordChanged"))
      complete({ requiresPasswordChange: false })
    } catch (error) {
      if (!applyPasswordServerErrors(form, "newPassword", error)) {
        toast.error(getErrorMessage(error))
      }
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
            <PasswordField
              control={form.control}
              name="newPassword"
              label={t("auth.newPassword")}
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
