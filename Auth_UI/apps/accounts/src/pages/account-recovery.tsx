import { zodResolver } from "@hookform/resolvers/zod"
import { TriangleAlert } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorCodes, getErrorMessage } from "@astoom/api/errors"
import { useAuth, type LoginResult } from "@astoom/auth/auth-context"
import { Alert, AlertDescription } from "@astoom/ui/alert"
import { AuthLayout } from "@astoom/ui/auth-layout"
import { Button } from "@astoom/ui/button"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { Spinner } from "@astoom/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface ExternalCredential {
  provider: string
  idToken: string
  nonce?: string
}

interface LocationState {
  /** Prefills the email field (password sign-in attempt carried it over). */
  email?: string
  /** Server-localized pending-deletion message, including the deadline. */
  message?: string
  /** Still-valid external credential from a failed provider sign-in. */
  external?: ExternalCredential
}

/**
 * Grace-period account recovery. Reached three ways: a password login that hit
 * the pending-deletion gate (email + message in state), an external login that
 * hit it (credential in state — restore is one click), or the recovery link
 * from the confirmation email (no state — plain email + password form).
 * Recovering cancels the deletion, restores the account, and signs the user in.
 */
export function AccountRecoveryPage() {
  const { t } = useTranslation()
  const { recoverAccount, recoverAccountExternal } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null
  const external = state?.external

  const [needsTwoFactor, setNeedsTwoFactor] = React.useState(false)
  const [externalCode, setExternalCode] = React.useState("")
  const [externalPending, setExternalPending] = React.useState(false)

  const schema = z.object({
    email: z
      .string()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
    password: z.string().min(1, t("validation.required")),
    twoFactorCode: z.string().optional(),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: state?.email ?? "",
      password: "",
      twoFactorCode: "",
    },
  })

  const onRecovered = React.useCallback(
    (result: LoginResult) => {
      // Defensive: recovery answers 2FA inline, but honor a challenge anyway.
      if (result.status === "twoFactorRequired") {
        navigate("/two-factor", {
          replace: true,
          state: { challengeToken: result.challengeToken, from: "/" },
        })
        return
      }
      toast.success(t("accountDeletion.restored"))
      navigate(result.requiresPasswordChange ? "/force-password-change" : "/", {
        replace: true,
      })
    },
    [navigate, t]
  )

  const handleRecoveryError = React.useCallback(
    (error: unknown) => {
      if (getErrorCodes(error).includes("User.TwoFactorRequired")) {
        setNeedsTwoFactor(true)
        return
      }
      toast.error(getErrorMessage(error))
    },
    []
  )

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      const result = await recoverAccount(
        values.email,
        values.password,
        values.twoFactorCode || undefined
      )
      onRecovered(result)
    } catch (error) {
      handleRecoveryError(error)
    }
  }

  const onSubmitExternal = async () => {
    if (!external) return
    setExternalPending(true)
    try {
      const result = await recoverAccountExternal(
        external.provider,
        external.idToken,
        external.nonce,
        externalCode || undefined
      )
      onRecovered(result)
    } catch (error) {
      handleRecoveryError(error)
    } finally {
      setExternalPending(false)
    }
  }

  return (
    <AuthLayout
      title={t("accountDeletion.recoveryTitle")}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <div className="flex flex-col gap-6">
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertDescription>
            {state?.message ?? t("accountDeletion.recoveryFallback")}
          </AlertDescription>
        </Alert>

        {external ? (
          <FieldGroup>
            {needsTwoFactor ? (
              <Field>
                <FieldLabel htmlFor="recovery-2fa">
                  {t("accountDeletion.twoFactorCode")}
                </FieldLabel>
                <Input
                  id="recovery-2fa"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  autoFocus
                  value={externalCode}
                  onChange={(event) => setExternalCode(event.target.value)}
                />
              </Field>
            ) : null}
            <Button
              className="w-full"
              disabled={externalPending}
              onClick={() => void onSubmitExternal()}
            >
              {externalPending ? <Spinner /> : null}
              {t("accountDeletion.restoreAccount")}
            </Button>
          </FieldGroup>
        ) : (
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
                      <FormLabel>{t("auth.password")}</FormLabel>
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
                {needsTwoFactor ? (
                  <FormField
                    control={form.control}
                    name="twoFactorCode"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>
                          {t("accountDeletion.twoFactorCode")}
                        </FormLabel>
                        <FormControl>
                          <Input
                            inputMode="numeric"
                            autoComplete="one-time-code"
                            autoFocus
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                ) : null}
                <Button
                  type="submit"
                  className="w-full"
                  disabled={form.formState.isSubmitting}
                >
                  {form.formState.isSubmitting ? (
                    <Spinner />
                  ) : null}
                  {t("accountDeletion.restoreAccount")}
                </Button>
              </FieldGroup>
            </form>
          </Form>
        )}
      </div>
    </AuthLayout>
  )
}
