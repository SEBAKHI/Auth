import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Loader2, ShieldCheck } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { CopyButton } from "@astoom/ui/common/copy-button"
import { QrCode } from "@astoom/ui/common/qr-code"
import { SecretRevealDialog } from "@astoom/ui/common/secret-reveal-dialog"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
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
import { Label } from "@astoom/ui/label"
import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import type { Schemas } from "@astoom/api/types"

function ChangePasswordCard() {
  const { t } = useTranslation()

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

  const mutation = useMutation({
    mutationFn: async (values: z.infer<typeof schema>) => {
      const { error } = await api.POST("/api/v1/Auth/change-password", {
        body: { ...values, terminateSessions: false },
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
                  <Loader2 className="animate-spin" />
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
      <CardContent className="space-y-4">
        {enabled ? (
          <div className="flex max-w-md flex-col gap-2 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-2">
              <Label htmlFor="disable-code">{t("auth.twoFactorCode")}</Label>
              <Input
                id="disable-code"
                value={disableCode}
                onChange={(e) => setDisableCode(e.target.value)}
                inputMode="numeric"
                autoComplete="one-time-code"
              />
            </div>
            <Button
              variant="destructive"
              onClick={() => disableMutation.mutate()}
              disabled={!disableCode || disableMutation.isPending}
            >
              {disableMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : null}
              {t("profile.disableTwoFactor")}
            </Button>
          </div>
        ) : setup ? (
          <div className="max-w-md space-y-3">
            <p className="text-sm text-muted-foreground">
              {t("profile.setupTwoFactorBody")}
            </p>
            <div className="flex justify-center">
              <QrCode value={setup.qrCodeUri} />
            </div>
            <div className="space-y-1">
              <Label>{t("profile.manualEntry")}</Label>
              <div className="flex items-center gap-2">
                <Input
                  readOnly
                  value={setup.manualEntryKey}
                  className="font-mono text-xs"
                />
                <CopyButton value={setup.manualEntryKey} />
              </div>
            </div>
            <div className="flex items-end gap-2">
              <div className="flex-1 space-y-2">
                <Label htmlFor="enable-code">{t("auth.twoFactorCode")}</Label>
                <Input
                  id="enable-code"
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                />
              </div>
              <Button
                onClick={() => enableMutation.mutate()}
                disabled={!code || enableMutation.isPending}
              >
                {enableMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : null}
                {t("auth.verify")}
              </Button>
            </div>
          </div>
        ) : (
          <Button
            onClick={() => setupMutation.mutate()}
            disabled={setupMutation.isPending}
          >
            {setupMutation.isPending ? (
              <Loader2 className="animate-spin" />
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

export function ProfileSecurity({ me }: { me: Schemas["UserDto"] }) {
  return (
    <div className="space-y-6">
      <ChangePasswordCard />
      <TwoFactorCard me={me} />
    </div>
  )
}
