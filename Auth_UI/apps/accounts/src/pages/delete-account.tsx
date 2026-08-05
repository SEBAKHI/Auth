import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation } from "@tanstack/react-query"
import { MailCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
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
import {
  OTP_CODE_LENGTH,
  OtpInput,
  RESEND_COOLDOWN_MS,
} from "@authsystem/ui/common/otp-input"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * Public no-login deletion wizard (compliance surface): email → emailed code →
 * generic confirmation. Every response is deliberately non-revealing — the
 * page never confirms whether an account exists.
 */
export function DeleteAccountPage() {
  const { t } = useTranslation()

  const [step, setStep] = React.useState<"email" | "code" | "done">("email")
  const [email, setEmail] = React.useState("")
  const [otp, setOtp] = React.useState("")
  const [cooldownUntil, setCooldownUntil] = React.useState<Date | null>(null)

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

  const requestMutation = useMutation({
    mutationFn: async (address: string) => {
      const { error } = await api.POST("/api/v1/Auth/deletion/request", {
        body: { email: address },
      })
      if (error) throw error
    },
    onSuccess: (_, address) => {
      setEmail(address)
      setOtp("")
      setStep("code")
      setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_MS))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const confirmMutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/Auth/deletion/confirm", {
        body: { email, otpCode: otp },
      })
      if (error) throw error
    },
    onSuccess: () => setStep("done"),
    onError: (error) => {
      setOtp("")
      toast.error(getErrorMessage(error))
    },
  })

  return (
    <AuthLayout
      title={t("accountDeletion.publicTitle")}
      subtitle={t("accountDeletion.publicSubtitle")}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      {step === "email" ? (
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((values) =>
              requestMutation.mutate(values.email)
            )}
          >
            <FieldGroup>
              <p className="text-sm text-muted-foreground">
                {t("accountDeletion.publicEmailBody")}
              </p>
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
                disabled={requestMutation.isPending}
              >
                {requestMutation.isPending ? (
                  <Spinner />
                ) : null}
                {t("accountDeletion.requestCode")}
              </Button>
            </FieldGroup>
          </form>
        </Form>
      ) : null}

      {step === "code" ? (
        <div className="flex flex-col items-center gap-4">
          <p className="text-center text-sm text-muted-foreground">
            {t("accountDeletion.publicCodeBody", { email })}
          </p>
          <OtpInput
            value={otp}
            onChange={setOtp}
            label={t("accountDeletion.verificationCode")}
            disabled={confirmMutation.isPending}
            autoFocus
          />
          <Button
            variant="destructive"
            className="w-full"
            disabled={otp.length < OTP_CODE_LENGTH || confirmMutation.isPending}
            onClick={() => confirmMutation.mutate()}
          >
            {confirmMutation.isPending ? (
              <Spinner />
            ) : null}
            {t("accountDeletion.confirmDeletion")}
          </Button>
          <ResendCodeButton
            availableAt={cooldownUntil}
            pending={requestMutation.isPending}
            onResend={() => requestMutation.mutate(email)}
          />
        </div>
      ) : null}

      {step === "done" ? (
        <div className="flex flex-col gap-4">
          <Alert>
            <MailCheck />
            <AlertTitle>{t("accountDeletion.publicDoneTitle")}</AlertTitle>
            <AlertDescription>
              {t("accountDeletion.publicDoneBody", { email })}
            </AlertDescription>
          </Alert>
          <Button asChild variant="outline" className="w-full">
            <Link to="/login">{t("auth.backToSignIn")}</Link>
          </Button>
        </div>
      ) : null}
    </AuthLayout>
  )
}
