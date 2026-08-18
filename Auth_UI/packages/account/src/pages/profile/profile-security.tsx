import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { ShieldCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { AuthenticatorApps } from "@authsystem/ui/common/authenticator-apps"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import { QrCode } from "@authsystem/ui/common/qr-code"
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
import { Field, FieldGroup, FieldLabel } from "@authsystem/ui/field"
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
import { SetPasswordPanel } from "@authsystem/auth/set-password-panel"
import { PASSWORD_LENGTH_FLOOR } from "@authsystem/api/constants"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { Spinner } from "@authsystem/ui/spinner"

function ChangePasswordCard() {
  const { t } = useTranslation()

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
    onError: (error) => toast.error(getErrorMessage(error)),
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

  const [setup, setup_set] = React.useState<Schemas["TwoFactorSetupResponse"]>()
  const [code, setCode] = React.useState("")
  const [disableCode, setDisableCode] = React.useState("")
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()

  const invalidateMe = () => queryClient.invalidateQueries({ queryKey: ["me"] })

  const setupMutation = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/setup")),
    onSuccess: (data) => setup_set(data),
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const enableMutation = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/auth/2fa/enable", { body: { code } })),
    onSuccess: (data) => {
      toast.success(t("profile.twoFactorEnabledToast"))
      setup_set(undefined)
      setCode("")
      void invalidateMe()
      if (data?.recoveryCodes?.length) {
        setRecoveryCodes(data.recoveryCodes.join("\n"))
      }
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const disableMutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/auth/2fa/disable", {
        body: { code: disableCode },
      })
      if (error) throw error
    },
    onSuccess: () => {
      toast.success(t("profile.twoFactorDisabledToast"))
      setDisableCode("")
      void invalidateMe()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

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
          <div className="flex max-w-md flex-col gap-2 sm:flex-row sm:items-end">
            <Field className="flex-1">
              <FieldLabel htmlFor="disable-code">
                {t("auth.twoFactorCode")}
              </FieldLabel>
              <Input
                id="disable-code"
                value={disableCode}
                onChange={(e) => setDisableCode(e.target.value)}
                inputMode="numeric"
                autoComplete="one-time-code"
              />
            </Field>
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
          </div>
        ) : setup ? (
          <div className="flex max-w-md flex-col gap-3">
            <p className="text-sm text-muted-foreground">
              {t("profile.setupTwoFactorBody")}
            </p>
            {/* Above the QR: the app has to exist before the code is any use. */}
            <AuthenticatorApps />
            <div className="flex justify-center">
              <QrCode value={setup.qrCodeUri} />
            </div>
            <Field>
              <FieldLabel htmlFor="manual-entry-key">
                {t("profile.manualEntry")}
              </FieldLabel>
              <div className="flex items-center gap-2">
                {/* A base32 secret typed into an authenticator by hand — pinned
                    LTR so an RTL profile cannot right-align or reorder it. */}
                <Input
                  id="manual-entry-key"
                  readOnly
                  dir="ltr"
                  value={setup.manualEntryKey}
                  className="font-mono text-xs"
                />
                <CopyButton value={setup.manualEntryKey} />
              </div>
            </Field>
            <div className="flex items-end gap-2">
              <Field className="flex-1">
                <FieldLabel htmlFor="enable-code">
                  {t("auth.twoFactorCode")}
                </FieldLabel>
                <Input
                  id="enable-code"
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                />
              </Field>
              <Button
                onClick={() => enableMutation.mutate()}
                disabled={!code || enableMutation.isPending}
              >
                {enableMutation.isPending ? (
                  <Spinner />
                ) : null}
                {t("auth.verify")}
              </Button>
            </div>
          </div>
        ) : (
          <Button
            className="w-fit"
            onClick={() => setupMutation.mutate()}
            disabled={setupMutation.isPending}
          >
            {setupMutation.isPending ? (
              <Spinner />
            ) : null}
            {t("profile.enableTwoFactor")}
          </Button>
        )}
      </CardContent>

      <SecretRevealDialog
        open={Boolean(recoveryCodes)}
        onOpenChange={(open) => !open && setRecoveryCodes(undefined)}
        title={t("profile.recoveryCodesTitle")}
        description={t("profile.recoveryCodesBody")}
        value={recoveryCodes ?? ""}
        multiline
      />
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
