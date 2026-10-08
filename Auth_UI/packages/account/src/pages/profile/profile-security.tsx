import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { ShieldCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { api } from "@authsystem/api/client"
import { PasswordField } from "@authsystem/auth/password-field"
import {
  applyPasswordServerErrors,
  passwordSchema,
} from "@authsystem/auth/password-rules"
import { ReauthenticateDialog } from "@authsystem/auth/reauthenticate-dialog"
import { SetPasswordPanel } from "@authsystem/auth/set-password-panel"
import { STALE_TWO_FACTOR_CODES } from "@authsystem/auth/two-factor-codes"
import { TwoFactorEnrollment } from "@authsystem/auth/two-factor-enrollment"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import type { Schemas } from "@authsystem/api/types"
import { Spinner } from "@authsystem/ui/spinner"
import { cn } from "@authsystem/ui/utils"

function ChangePasswordCard() {
  const { t } = useTranslation()
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

  const mutation = useMutation({
    mutationFn: async (values: z.infer<typeof schema>) => {
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
    },
    onSuccess: () => {
      toast.success(t("profile.passwordChanged"))
      form.reset()
    },
    onError: (error) => {
      if (!applyPasswordServerErrors(form, "newPassword", error)) {
        toast.error(getErrorMessage(error))
      }
    },
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">
          {t("profile.changePassword")}
        </CardTitle>
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <FieldGroup className="max-w-md">
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
                className="w-fit"
                disabled={mutation.isPending}
              >
                {mutation.isPending ? (
                  <Spinner />
                ) : null}
                {t("profile.changePassword")}
              </Button>
            </FieldGroup>
          </form>
        </Form>
      </CardContent>
    </Card>
  )
}

function TwoFactorCard({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const enabled = Boolean(me.twoFactorEnabled)

  const [disableCode, setDisableCode] = React.useState("")
  // A recovery code switches the factor off for a user whose phone is gone.
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)

  const invalidateMe = () => queryClient.invalidateQueries({ queryKey: ["me"] })

  /*
   * One answer to a failure of disable, keyed by the published code — never by
   * the status: TwoFactor.LockedOut is a 403 as well, and only a stale sign-in can
   * be fixed by signing in again. Switching it on answers its own failures
   * (TwoFactorEnrollment).
   */
  const onDisableError = (error: unknown) => {
    const codes = getErrorCodes(error)
    if (codes.includes("Auth.ReauthenticationRequired")) {
      setReauthenticateOpen(true)
      return
    }

    toast.error(getErrorMessage(error))

    if (STALE_TWO_FACTOR_CODES.some((stale) => codes.includes(stale))) {
      void invalidateMe()
    }
  }

  const disableMutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/auth/2fa/disable", {
        body: { code: disableCode, useRecoveryCode },
      })
      if (error) throw error
    },
    onSuccess: () => {
      toast.success(t("profile.twoFactorDisabledToast"))
      setDisableCode("")
      setUseRecoveryCode(false)
      void invalidateMe()
    },
    onError: onDisableError,
  })

  const toggleRecoveryCode = () => {
    setUseRecoveryCode((previous) => !previous)
    setDisableCode("")
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ShieldCheck className="size-4" />
          {t("profile.twoFactor")}
          <Badge variant={enabled ? "default" : "secondary"}>
            {enabled ? t("common.enabled") : t("common.disabled")}
          </Badge>
        </CardTitle>
        <CardDescription>
          {enabled
            ? t("profile.twoFactorEnabled")
            : t("profile.twoFactorDisabled")}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {enabled ? (
          <FieldGroup className="max-w-md">
            <Field>
              <FieldLabel htmlFor="disable-code">
                {useRecoveryCode
                  ? t("auth.recoveryCode")
                  : t("auth.twoFactorCode")}
              </FieldLabel>
              {/* A recovery code is neither numeric nor a one-time code the
                  browser should offer to fill; it is transcribed exactly, so it
                  is pinned LTR like the sign-in page's recovery field. */}
              <Input
                id="disable-code"
                value={disableCode}
                onChange={(e) => setDisableCode(e.target.value)}
                inputMode={useRecoveryCode ? undefined : "numeric"}
                autoComplete={useRecoveryCode ? "off" : "one-time-code"}
                dir={useRecoveryCode ? "ltr" : undefined}
                className={cn(useRecoveryCode && "font-mono")}
              />
              <FieldDescription>
                {t("profile.twoFactorDisableSignsOutOthers")}
              </FieldDescription>
            </Field>
            <Field orientation="horizontal">
              <Button
                variant="destructive"
                onClick={() => disableMutation.mutate()}
                disabled={!disableCode || disableMutation.isPending}
              >
                {disableMutation.isPending ? (
                  <Spinner />
                ) : null}
                {t("profile.disableTwoFactor")}
              </Button>
              <Button
                type="button"
                variant="link"
                className="text-muted-foreground"
                onClick={toggleRecoveryCode}
              >
                {useRecoveryCode
                  ? t("auth.useAuthenticatorCode")
                  : t("auth.useRecoveryCode")}
              </Button>
            </Field>
          </FieldGroup>
        ) : (
          <TwoFactorEnrollment
            account={me}
            onEnabled={(codes) => {
              void invalidateMe()
              if (codes.length) setRecoveryCodes(codes.join("\n"))
            }}
            onStale={() => void invalidateMe()}
          />
        )}
      </CardContent>

      {/* Outside the branch above: the account refetch that flips the card to
          "enabled" must not take the codes away before they are saved. */}
      <SecretRevealDialog
        open={Boolean(recoveryCodes)}
        onOpenChange={(open) => !open && setRecoveryCodes(undefined)}
        title={t("profile.recoveryCodesTitle")}
        description={t("profile.recoveryCodesBody")}
        value={recoveryCodes ?? ""}
        multiline
      />

      {/* Mounted only while needed, so closing it resets it. */}
      {reauthenticateOpen ? (
        <ReauthenticateDialog open onOpenChange={setReauthenticateOpen} />
      ) : null}
    </Card>
  )
}

function SetPasswordCard({ email }: { email: string }) {
  const { t } = useTranslation()

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{t("profile.setPassword")}</CardTitle>
      </CardHeader>
      <CardContent>
        <SetPasswordPanel email={email} />
      </CardContent>
    </Card>
  )
}

export function ProfileSecurity({ me }: { me: Schemas["UserDto"] }) {
  /*
   * An account created by signing in with Google has no password, so the change
   * form below asked it for a current password it could never supply - the form
   * was not merely awkward, it was unsubmittable. `hasPassword` has been on
   * UserDto and on this very query all along with nothing reading it.
   *
   * Compared against `false` rather than negated: the generated type marks it
   * optional, so an API that stops sending it would otherwise hide the change
   * form from everyone. Failing back to the change form is the safe direction.
   */
  const hasNoPassword = me.hasPassword === false && Boolean(me.email)

  return (
    <div className="flex flex-col gap-6">
      {hasNoPassword ? (
        <SetPasswordCard email={me.email!} />
      ) : (
        <ChangePasswordCard />
      )}
      <TwoFactorCard me={me} />
    </div>
  )
}
