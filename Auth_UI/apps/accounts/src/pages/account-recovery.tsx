import { zodResolver } from "@hookform/resolvers/zod"
import { TriangleAlert } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { useAuth, type LoginResult } from "@authsystem/auth/auth-context"
import { ExternalProviders } from "@authsystem/auth/external/external-providers"
import type { ExternalCredential } from "@authsystem/auth/external/recovery-navigation"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
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
import { Spinner } from "@authsystem/ui/spinner"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

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
 * from the confirmation email. Recovering cancels the deletion, restores the
 * account, and signs the user in.
 *
 * The emailed link is the case that shaped this page. It is a bare URL with no
 * token, no address and no hint of how the account signs in, so it arrives with
 * no state at all — and for as long as this screen offered only an email and
 * password form, it was a dead end for exactly the accounts most likely to
 * follow it. An account created with "Continue with Google" has no password
 * hash, so the form could never be satisfied, while the mail it came from
 * promised the account could be restored at any time before the deadline.
 *
 * Hence the provider buttons below the form. They do not sign in — a pending
 * deletion would refuse that anyway — they capture the credential into
 * `external`, which drops the page into the one-click branch it already had.
 */
export function AccountRecoveryPage() {
  const { t } = useTranslation()
  const { recoverAccount, recoverAccountExternal } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as LocationState | null

  // Seeded from router state (arrived from a failed sign-in) and otherwise
  // filled by the provider buttons (arrived cold from the email).
  const [external, setExternal] = React.useState<ExternalCredential | undefined>(
    state?.external
  )
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
        {/*
          Two sentences with two different jobs, and only the first is always
          true. The alert states the FACT — the account is deactivated and dated
          for deletion — which holds on every path into this screen. The
          instruction to confirm credentials describes the form, so it appears
          only while the form does.

          Conflating them is what produced the defect: the text was picked by
          how the visitor ARRIVED (router state present or not) rather than by
          what is on screen, so opening the emailed link and then signing in
          with a provider left "confirm your credentials below" sitting above a
          single button and no fields to confirm anything in.

          The two fact variants are one sentence, not two messages: the server's
          carries the exact deadline because the failed sign-in told us; the
          local one cannot, because a cold-opened link identifies no account.
        */}
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertDescription>
            <p>{state?.message ?? t("accountDeletion.recoveryFallback")}</p>
            {external ? null : (
              <p>{t("accountDeletion.confirmCredentials")}</p>
            )}
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
                          dir="ltr"
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

        {/*
          The other way in, for an account that has no password to type above.
          Capture mode, not sign-in: the credential lands in `external` and the
          branch above takes over, so two-factor and the restore call are the
          same code that already served the sign-in route. Renders nothing when
          no provider is enabled, and nothing once a credential is held.
        */}
        {external ? null : (
          <ExternalProviders
            onCredential={(credential) => setExternal(credential)}
          />
        )}
      </div>
    </AuthLayout>
  )
}
