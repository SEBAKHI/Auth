import { zodResolver } from "@hookform/resolvers/zod"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { privacyPolicyUrl } from "@authsystem/api/env"
import { getErrorMessage } from "@authsystem/api/errors"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
import { FieldGroup } from "@authsystem/ui/field"
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import { startSignUpCode } from "@authsystem/auth/email-code"
import { ExternalProviders } from "@authsystem/auth/external/external-providers"
import { useFlowBranding } from "@authsystem/auth/flow-branding"
import { useReloadOnRestore } from "@authsystem/auth/reload-on-restore"
import { getValidReturnTo } from "@authsystem/auth/return-to"

import { savePendingRegistration } from "./registration-flow"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * First of the three sign-up screens: the address, and nothing else.
 *
 * Nothing about the person is asked for until the address has proven itself
 * with the code the server mails to it, so a form here that took a name and a
 * password would only be collecting what the server will refuse to store. The
 * server answers this request identically for every address — free, already
 * registered, or reserved — and so does this screen: every address goes on to
 * the code screen. The message differs, seen only by the mailbox's holder: a
 * registration code, a sign-in code for an address that already has an account
 * (the code screen then signs it in, OI-215), or a notice.
 *
 * The provider buttons sit OUTSIDE the form on purpose. A Google or Apple
 * identity proves its own mailbox and skips the code entirely.
 *
 * Back to this page asks the server again rather than reviving it (OI-150).
 */
export function RegisterPage() {
  useReloadOnRestore()
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  // A pending authorize request rides in the query string across all three
  // screens; each link and navigation carries it forward unchanged.
  const { search } = useLocation()
  const { layout: appHeader } = useFlowBranding(getValidReturnTo(search))

  const schema = z.object({
    email: z
      .string()
      .trim()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
  })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "" },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      // The sign-in page's start (OI-215): an address that already has an
      // account receives a sign-in code, and the code screen signs it in.
      savePendingRegistration(
        await startSignUpCode(values.email, i18n.language)
      )
      navigate({ pathname: "/register/verify", search })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout
      title={t("auth.registerTitle")}
      subtitle={t("auth.registerSubtitle")}
      {...appHeader}
      footer={
        <span>
          {t("auth.haveAccount")}{" "}
          <Link
            to={{ pathname: "/login", search }}
            className="underline-offset-4 hover:underline"
          >
            {t("auth.signIn")}
          </Link>
        </span>
      }
      // GDPR Art. 13(1) fixes the disclosure at the moment personal data is
      // obtained, and registration is that moment — this page offered no route
      // to the notice at all. A bare link, not a combined "I have read and
      // agree" box: KVKK principle decision 2026/347 requires the disclosure to
      // be separate from consent, and permits asking only for confirmation of
      // reading, never approval of its content.
      pageFooter={
        <a
          href={privacyPolicyUrl(i18n.language)}
          className="underline-offset-4 hover:underline"
        >
          {t("auth.privacyPolicy")}
        </a>
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
                  <FormDescription>{t("auth.registerEmailHint")}</FormDescription>
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
                <>
                  <Spinner data-icon="inline-start" />
                  {t("auth.sendingCode")}
                </>
              ) : (
                t("auth.sendCode")
              )}
            </Button>
          </FieldGroup>
        </form>
      </Form>
      <ExternalProviders recoveryPath="/account-recovery" />
    </AuthLayout>
  )
}
