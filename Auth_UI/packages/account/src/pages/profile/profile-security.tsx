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
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@authsystem/ui/input-group"
import { api } from "@authsystem/api/client"
import { PasswordField } from "@authsystem/auth/password-field"
import {
  applyPasswordServerErrors,
  passwordSchema,
} from "@authsystem/auth/password-rules"
import { ReauthenticateDialog } from "@authsystem/auth/reauthenticate-dialog"
import { SetPasswordPanel } from "@authsystem/auth/set-password-panel"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"
import { unwrap } from "@authsystem/api/helpers"
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

/**
 * Answers after which this card's own picture of two-factor is out of date:
 * another tab or device switched it on or off, or replaced the secret this setup
 * showed.
 */
const STALE_TWO_FACTOR_CODES: readonly PublishedErrorCode[] = [
  "User.TwoFactorAlreadyEnabled",
  "User.TwoFactorNotEnabled",
  "TwoFactor.SetupRequired",
]

/**
 * Whole minutes until an emailed code expires, by the server's expiry. Bounded
 * to the range the server allows (Email:OtpExpirationMinutes is 1-60), so a
 * badly set device clock cannot print a nonsense number; undefined when the
 * answer carried no expiry.
 */
function minutesUntil(
  expiresAt: string | null | undefined
): number | undefined {
  const milliseconds = expiresAt ? Date.parse(expiresAt) - Date.now() : NaN
  if (!Number.isFinite(milliseconds)) return undefined
  return Math.min(60, Math.max(1, Math.round(milliseconds / 60_000)))
}

function TwoFactorCard({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const enabled = Boolean(me.twoFactorEnabled)

  const [setup, setup_set] = React.useState<Schemas["TwoFactorSetupResponse"]>()
  const [code, setCode] = React.useState("")
  const [disableCode, setDisableCode] = React.useState("")
  // A recovery code switches the factor off for a user whose phone is gone.
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)
  // The account's FIRST factor, while email is on, also needs a code sent to its
  // address: shown when setup says so, or when enable answers that it is needed.
  const [emailStep, setEmailStep] = React.useState(false)
  const [emailCode, setEmailCode] = React.useState("")
  const [emailCodeSent, setEmailCodeSent] = React.useState<{
    sentTo: string
    minutes?: number
  }>()
  // Every send mails a new code and retires the previous one, so a second click
  // landing before the button re-renders disabled would make the first email's
  // code useless: the latch is set synchronously, in the handler.
  const sendingEmailCode = React.useRef(false)

  const invalidateMe = () => queryClient.invalidateQueries({ queryKey: ["me"] })

  const clearSetup = () => {
    setup_set(undefined)
    setCode("")
    setEmailStep(false)
    setEmailCode("")
    setEmailCodeSent(undefined)
  }

  /*
   * One answer to a failure for setup, the email code, enable and disable, keyed
   * by the published code — never by the status: TwoFactor.LockedOut is a 403 as
   * well, and only a stale sign-in can be fixed by signing in again.
   */
  const onTwoFactorError = (error: unknown) => {
    const codes = getErrorCodes(error)
    if (codes.includes("Auth.ReauthenticationRequired")) {
      setReauthenticateOpen(true)
      return
    }

    toast.error(getErrorMessage(error))

    // A setting changed after setup: the server now wants the emailed code.
    if (codes.includes("TwoFactor.EmailCodeRequired")) {
      setEmailStep(true)
    }

    if (STALE_TWO_FACTOR_CODES.some((stale) => codes.includes(stale))) {
      clearSetup()
      void invalidateMe()
    }
  }

  const setupMutation = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/setup")),
    onSuccess: (data) => {
      setup_set(data)
      setCode("")
      // An API without the member (an older build) needs no email step.
      setEmailStep(data?.emailCodeRequired === true)
      setEmailCode("")
      setEmailCodeSent(undefined)
    },
    onError: onTwoFactorError,
  })

  const sendEmailCodeMutation = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/email-code")),
    onSuccess: (data) => {
      // Email was switched off, or the rule, since setup: no code is needed.
      if (data?.emailCodeRequired !== true) {
        setEmailStep(false)
        setEmailCodeSent(undefined)
        return
      }
      setEmailCodeSent({
        sentTo: data.sentTo ?? "",
        minutes: minutesUntil(data.expiresAt),
      })
    },
    onError: onTwoFactorError,
  })

  const sendEmailCode = () => {
    if (sendingEmailCode.current) return
    sendingEmailCode.current = true
    sendEmailCodeMutation.mutate(undefined, {
      onSettled: () => {
        sendingEmailCode.current = false
      },
    })
  }

  const enableMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/auth/2fa/enable", {
          body: emailStep ? { code, emailCode } : { code },
        })
      ),
    onSuccess: (data) => {
      toast.success(t("profile.twoFactorEnabledToast"))
      clearSetup()
      void invalidateMe()
      if (data?.recoveryCodes?.length) {
        setRecoveryCodes(data.recoveryCodes.join("\n"))
      }
    },
    onError: onTwoFactorError,
  })

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
    onError: onTwoFactorError,
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
            {emailStep ? (
              <Field>
                <FieldLabel htmlFor="enable-email-code">
                  {t("profile.twoFactorEmailCode")}
                </FieldLabel>
                <InputGroup>
                  <InputGroupInput
                    id="enable-email-code"
                    value={emailCode}
                    onChange={(e) => setEmailCode(e.target.value)}
                    inputMode="numeric"
                    autoComplete="one-time-code"
                  />
                  <InputGroupAddon align="inline-end">
                    <InputGroupButton
                      onClick={sendEmailCode}
                      disabled={sendEmailCodeMutation.isPending}
                    >
                      {sendEmailCodeMutation.isPending ? (
                        <Spinner data-icon="inline-start" />
                      ) : null}
                      {emailCodeSent
                        ? t("profile.twoFactorEmailCodeResend")
                        : t("profile.twoFactorEmailCodeSend")}
                    </InputGroupButton>
                  </InputGroupAddon>
                </InputGroup>
                {/* Says where the code went — the address it was sent to — so a
                    code that does not arrive can be told from one sent elsewhere. */}
                <FieldDescription aria-live="polite">
                  {emailCodeSent
                    ? [
                        t("profile.twoFactorEmailCodeSentTo", {
                          email: emailCodeSent.sentTo,
                        }),
                        emailCodeSent.minutes === undefined
                          ? null
                          : t("profile.twoFactorEmailCodeExpiresIn", {
                              minutes: emailCodeSent.minutes,
                            }),
                      ]
                        .filter(Boolean)
                        .join(" ")
                    : t("profile.twoFactorEmailCodeHint")}
                </FieldDescription>
              </Field>
            ) : null}
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
                disabled={
                  !code || (emailStep && !emailCode) || enableMutation.isPending
                }
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
