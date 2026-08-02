import { zodResolver } from "@hookform/resolvers/zod"
import { MailCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@authsystem/api/client"
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
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Spinner } from "@authsystem/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface SentState {
  maskedEmail: string
  expiresAt: Date | null
}

/**
 * Requests a password reset link. On success the page stays put and reports that
 * the mail was sent - the link in that mail is the only way onward, so there is
 * nothing for the user to type here.
 */
export function ForgotPasswordPage() {
  const { t } = useTranslation()
  const [sent, setSent] = React.useState<SentState | null>(null)

  const schema = z.object({
    email: z
      .string()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "" },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const { data, error } = await api.POST("/api/v1/Auth/forgot-password", {
        body: { email: values.email },
      })
      if (error) throw error
      setSent({
        // The response is deliberately identical for unknown addresses, so this
        // reveals nothing about whether the account exists.
        maskedEmail: data?.maskedEmail ?? values.email,
        expiresAt: data?.expiresAt ? new Date(data.expiresAt) : null,
      })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  if (sent) {
    return <ResetLinkSent sent={sent} onResend={() => setSent(null)} />
  }

  return (
    <AuthLayout
      title={t("auth.forgotTitle")}
      subtitle={t("auth.forgotSubtitle")}
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
            <Button
              type="submit"
              className="w-full"
              disabled={form.formState.isSubmitting}
            >
              {form.formState.isSubmitting ? (
                <Spinner />
              ) : null}
              {t("auth.sendResetLink")}
            </Button>
          </FieldGroup>
        </form>
      </Form>
    </AuthLayout>
  )
}

function ResetLinkSent({
  sent,
  onResend,
}: {
  sent: SentState
  onResend: () => void
}) {
  const { t } = useTranslation()
  const countdown = useCountdown(sent.expiresAt)

  return (
    <AuthLayout
      title={t("auth.resetLinkSentTitle")}
      subtitle={t("auth.resetLinkSentDescription", { email: sent.maskedEmail })}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <div className="flex flex-col items-center gap-4">
        <MailCheck className="size-10 text-muted-foreground" aria-hidden />

        {sent.expiresAt ? (
          countdown.expired ? (
            <p className="text-sm text-destructive">
              {t("auth.resetLinkExpired")}
            </p>
          ) : (
            <p className="text-sm text-muted-foreground tabular-nums">
              {t("auth.resetLinkExpiresIn", { time: countdown.label })}
            </p>
          )
        ) : null}

        <Button
          type="button"
          variant="link"
          className="text-muted-foreground"
          onClick={onResend}
        >
          {t("auth.resendResetLink")}
        </Button>
      </div>
    </AuthLayout>
  )
}
