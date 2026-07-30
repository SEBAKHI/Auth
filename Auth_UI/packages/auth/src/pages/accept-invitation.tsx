import { zodResolver } from "@hookform/resolvers/zod"
import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { Button } from "@astoom/ui/button"
import { Spinner } from "@astoom/ui/spinner"
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
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import type { Schemas } from "@astoom/api/types"
import { useAuth } from "@astoom/auth/auth-context"
import { getErrorMessage } from "@astoom/api/errors"
import { formatDateTime } from "@astoom/ui/format"
import { AuthLayout } from "@astoom/ui/auth-layout"

type InvitationPreview = Schemas["InvitationPreviewDto"]

function InvitationSummary({ preview }: { preview: InvitationPreview }) {
  const { t } = useTranslation()
  const rows: Array<[string, string | null | undefined]> = [
    [t("auth.invitationOrg"), preview.organizationName],
    [t("auth.invitationRole"), preview.roleName],
    [t("auth.invitationInvitedBy"), preview.invitedByName],
    [t("auth.email"), preview.email],
    [t("auth.invitationExpires"), formatDateTime(preview.expiresAt)],
  ]
  return (
    <dl className="flex flex-col gap-2 text-sm">
      {rows
        .filter(([, value]) => Boolean(value))
        .map(([label, value]) => (
          <div key={label} className="flex items-center justify-between gap-4">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="font-medium">{value}</dd>
          </div>
        ))}
    </dl>
  )
}

/** Message-only state (invalid, expired, already accepted, …). */
function InvitationNotice({ message }: { message: string }) {
  const { t } = useTranslation()
  return (
    <AuthLayout
      title={t("auth.invitationTitle")}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <p className="text-sm text-muted-foreground">{message}</p>
    </AuthLayout>
  )
}

/** Existing-account path: sign in first, then accept with one click. */
function AcceptExisting({
  token,
  preview,
}: {
  token: string
  preview: InvitationPreview
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { status, user, logout } = useAuth()
  const [accepting, setAccepting] = React.useState(false)

  const accept = async () => {
    setAccepting(true)
    try {
      await unwrap(
        api.POST("/api/v1/Invitations/{token}/accept", {
          params: { path: { token } },
        })
      )
      toast.success(t("auth.invitationAccepted"))
      navigate("/", { replace: true })
    } catch (error) {
      toast.error(getErrorMessage(error))
      setAccepting(false)
    }
  }

  const signInToAccept = () =>
    navigate("/login", {
      state: {
        from: {
          pathname: "/accept-invitation",
          search: `?token=${encodeURIComponent(token)}`,
        },
        email: preview.email,
      },
    })

  const emailMismatch =
    status === "authenticated" &&
    user?.email?.toLowerCase() !== preview.email?.toLowerCase()

  return (
    <AuthLayout
      title={t("auth.invitationTitle")}
      subtitle={t("auth.invitationSubtitle")}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <FieldGroup>
        <InvitationSummary preview={preview} />
        {status === "authenticated" ? (
          emailMismatch ? (
            <>
              <p className="text-sm text-destructive">
                {t("auth.invitationEmailMismatch", {
                  current: user?.email ?? "",
                  invited: preview.email ?? "",
                })}
              </p>
              <Button variant="outline" className="w-full" onClick={logout}>
                {t("auth.invitationSignOut")}
              </Button>
            </>
          ) : (
            <Button className="w-full" disabled={accepting} onClick={accept}>
              {accepting ? (
                <>
                  <Spinner />
                  {t("auth.invitationAccepting")}
                </>
              ) : (
                t("auth.invitationAccept")
              )}
            </Button>
          )
        ) : (
          <Button className="w-full" onClick={signInToAccept}>
            {t("auth.invitationSignInToAccept")}
          </Button>
        )}
      </FieldGroup>
    </AuthLayout>
  )
}

/** New-account path: register with the invited email, then sign in. */
function RegisterAndJoin({
  token,
  preview,
}: {
  token: string
  preview: InvitationPreview
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const schema = z
    .object({
      firstName: z.string().min(1, t("validation.required")),
      lastName: z.string().min(1, t("validation.required")),
      password: z.string().min(8, t("validation.minLength", { count: 8 })),
      confirmPassword: z.string().min(1, t("validation.required")),
    })
    .refine((data) => data.password === data.confirmPassword, {
      message: t("validation.passwordMismatch"),
      path: ["confirmPassword"],
    })

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: "",
      lastName: "",
      password: "",
      confirmPassword: "",
    },
  })

  const onSubmit = async (values: z.infer<typeof schema>) => {
    try {
      await unwrap(
        api.POST("/api/v1/Invitations/{token}/register", {
          params: { path: { token } },
          body: {
            password: values.password,
            firstName: values.firstName,
            lastName: values.lastName,
          },
        })
      )
      toast.success(t("auth.invitationRegisterSuccess"))
      navigate("/login", { state: { email: preview.email } })
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <AuthLayout
      title={t("auth.invitationTitle")}
      subtitle={t("auth.invitationCreateAccount", {
        org: preview.organizationName ?? "",
      })}
      footer={
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t("auth.backToSignIn")}
        </Link>
      }
    >
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <FieldGroup>
            <InvitationSummary preview={preview} />
            <FormField
              control={form.control}
              name="firstName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.invitationFirstName")}</FormLabel>
                  <FormControl>
                    <Input
                      autoComplete="given-name"
                      autoFocus
                      placeholder="Sara"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="lastName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.invitationLastName")}</FormLabel>
                  <FormControl>
                    <Input
                      autoComplete="family-name"
                      placeholder="Al-Rashid"
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
              name="confirmPassword"
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
                <>
                  <Spinner />
                  {t("auth.invitationCreating")}
                </>
              ) : (
                t("auth.invitationCreateAndJoin")
              )}
            </Button>
          </FieldGroup>
        </form>
      </Form>
    </AuthLayout>
  )
}

export function AcceptInvitationPage() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const token = searchParams.get("token") ?? ""

  const query = useQuery({
    queryKey: ["invitation-preview", token],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Invitations/{token}", {
          params: { path: { token } },
        })
      ),
    enabled: token.length > 0,
    retry: false,
  })

  if (!token || query.isError) {
    return <InvitationNotice message={t("auth.invitationInvalid")} />
  }

  if (query.isPending) {
    return (
      <AuthLayout title={t("auth.invitationTitle")}>
        <div className="flex justify-center py-6">
          <Spinner className="text-muted-foreground" />
        </div>
      </AuthLayout>
    )
  }

  const preview = query.data

  if (preview.isExpired) {
    return <InvitationNotice message={t("auth.invitationExpired")} />
  }
  if (preview.status === "Accepted") {
    return <InvitationNotice message={t("auth.invitationAlreadyAccepted")} />
  }
  if (preview.status !== "Pending") {
    return <InvitationNotice message={t("auth.invitationUnavailable")} />
  }

  return preview.userExists ? (
    <AcceptExisting token={token} preview={preview} />
  ) : (
    <RegisterAndJoin token={token} preview={preview} />
  )
}
