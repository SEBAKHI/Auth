import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { Button } from "@authsystem/ui/button"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { Spinner } from "@authsystem/ui/spinner"
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
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import type { Schemas } from "@authsystem/api/types"
import { useAuth } from "@authsystem/auth/auth-context"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime } from "@authsystem/ui/format"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { routePath } from "@authsystem/ui/route-path"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"

import { useSignInOptions } from "@authsystem/ui/branding"

import { startEmailCode, type PendingEmailCode } from "../email-code"
import { launchApplication } from "../launch-application"
import { useLoginCompletion } from "../login-completion"
import { PasswordField } from "../password-field"
import { applyPasswordServerErrors, passwordSchema } from "../password-rules"

type InvitationPreview = Schemas["InvitationPreviewDto"]
type AcceptResult = Schemas["InvitationAcceptResultDto"]

/**
 * This tab's own note that its "Sign in to accept" button was pressed (OI-192):
 * back from the sign-in, the page accepts that invitation by itself, once,
 * instead of asking for a second click.
 *
 * Per tab (sessionStorage) and short-lived, and it names the invitation by its
 * id, never by its token. Only the button writes it, so a link alone, however
 * crafted, never accepts anything: whoever follows one still has to press a
 * button on this page first.
 */
export const ACCEPT_AFTER_SIGN_IN_KEY = "auth.invitation.acceptAfterSignIn"
const ACCEPT_AFTER_SIGN_IN_TTL_MS = 15 * 60 * 1000

function rememberAcceptAfterSignIn(invitationId: string | undefined) {
  if (!invitationId) return
  try {
    sessionStorage.setItem(
      ACCEPT_AFTER_SIGN_IN_KEY,
      JSON.stringify({
        invitationId,
        expiresAt: Date.now() + ACCEPT_AFTER_SIGN_IN_TTL_MS,
      })
    )
  } catch {
    // Storage refused: the person clicks Accept once more, as before.
  }
}

/**
 * Whether the note names this invitation and is still fresh. Removed as it is
 * read, before anything is sent, so it can accept at most once; one that names
 * another invitation is left for that invitation's page.
 */
function takeAcceptAfterSignIn(invitationId: string | undefined): boolean {
  if (!invitationId) return false
  try {
    const raw = sessionStorage.getItem(ACCEPT_AFTER_SIGN_IN_KEY)
    if (!raw) return false
    const note = JSON.parse(raw) as {
      invitationId?: unknown
      expiresAt?: unknown
    }
    const expired =
      typeof note.expiresAt !== "number" || note.expiresAt <= Date.now()
    if (note.invitationId !== invitationId && !expired) return false
    sessionStorage.removeItem(ACCEPT_AFTER_SIGN_IN_KEY)
    return note.invitationId === invitationId && !expired
  } catch {
    return false
  }
}

/**
 * Drops the note without reading it. Called once this tab is known to be
 * signed out (OI-195): the note exists for the trip through the sign-in, so a
 * page that loads signed out is not that trip's return, and a later signed-in
 * visit must not find it and accept without a click.
 */
function forgetAcceptAfterSignIn() {
  try {
    sessionStorage.removeItem(ACCEPT_AFTER_SIGN_IN_KEY)
  } catch {
    // Storage refused: there is no note to forget either.
  }
}

/**
 * The application role was not given: the inviter could no longer give it. The
 * wire carries the member's name ("Dropped"); the generated schema types the
 * enum as a number, so the value is compared as what it is.
 */
function wasRoleDropped(outcome: unknown): boolean {
  return outcome === "Dropped"
}

/** The organization just joined, mounted at the same path in both apps. */
function organizationPage(organizationId: string | null | undefined) {
  return organizationId
    ? routePath`/organizations/${organizationId}`
    : "/organizations"
}

function ApplicationName({ preview }: { preview: InvitationPreview }) {
  const resolvedTheme = useResolvedTheme()
  return (
    <span className="flex items-center gap-2">
      <EntityAvatar
        src={pickLogo(
          preview.applicationLogoUrl ?? null,
          preview.applicationLogoUrlDark ?? null,
          resolvedTheme
        )}
        name={preview.applicationName}
        size="sm"
        fit="contain"
      />
      {preview.applicationName}
    </span>
  )
}

function InvitationSummary({ preview }: { preview: InvitationPreview }) {
  const { t } = useTranslation()
  const rows: Array<[string, React.ReactNode]> = [
    [t("auth.invitationOrg"), preview.organizationName],
    [t("auth.invitationRole"), preview.roleName],
    // Where accepting takes the invitee, when the invitation carries an
    // application role (OI-192).
    [
      t("auth.invitationApplication"),
      preview.applicationName ? <ApplicationName preview={preview} /> : null,
    ],
    [t("auth.invitationApplicationRole"), preview.applicationRoleName],
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

/**
 * Joined, but without the application role: the inviter could no longer give
 * it. Says so, and who can fix it, and leads on to the organization: never a
 * page with nothing to do.
 */
function InvitationJoined({
  result,
  preview,
}: {
  result: AcceptResult
  preview: InvitationPreview
}) {
  const { t } = useTranslation()
  const org = result.organizationName || preview.organizationName || ""
  const app = result.application?.name ?? preview.applicationName ?? null
  return (
    <AuthLayout title={t("auth.invitationJoinedTitle")}>
      <FieldGroup>
        <p
          className="text-sm text-muted-foreground"
          data-testid="invitation-role-dropped"
        >
          {app
            ? t("auth.invitationRoleDropped", { org, app })
            : t("auth.invitationRoleDroppedNoApp", { org })}
        </p>
        <Button asChild className="w-full">
          <Link to={organizationPage(result.organizationId)} replace>
            {t("auth.invitationOpenOrganization")}
          </Link>
        </Button>
      </FieldGroup>
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
  const [joined, setJoined] = React.useState<AcceptResult | null>(null)
  // Set in the handler itself: the disabled button is a frame behind a fast
  // second click, and an automatic acceptance must not race a manual one.
  const sending = React.useRef(false)

  const { mutate, isPending, isSuccess } = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/Invitations/{token}/accept", {
          params: { path: { token } },
        })
      ),
    onSuccess: (result) => {
      toast.success(t("auth.invitationAccepted"))
      // Into the application, when the invitee now holds its role and it has
      // somewhere to start a sign-in (OI-192). The address is the server's,
      // from the application's registration, and checked again here.
      if (launchApplication(result.launchUrl)) return
      if (wasRoleDropped(result.applicationRoleOutcome)) {
        setJoined(result)
        return
      }
      navigate(organizationPage(result.organizationId), { replace: true })
    },
    onError: (error) => {
      toast.error(getErrorMessage(error))
      sending.current = false
    },
  })
  // Still "accepting" once it succeeded: the page is leaving.
  const accepting = isPending || isSuccess

  const accept = React.useCallback(() => {
    if (sending.current) return
    sending.current = true
    mutate()
  }, [mutate])

  const emailMismatch =
    status === "authenticated" &&
    user?.email?.toLowerCase() !== preview.email?.toLowerCase()

  // Back from the sign-in this tab's "Sign in to accept" started: accept once,
  // by itself, so the invitee does not have to click Accept a second time. The
  // note is taken (and so removed) as soon as the account is known, whether or
  // not it is the invited one; only the invited one goes on to accept.
  const accountEmail = status === "authenticated" ? user?.email : undefined
  React.useEffect(() => {
    if (!accountEmail) return
    if (!takeAcceptAfterSignIn(preview.id)) return
    if (accountEmail.toLowerCase() !== preview.email?.toLowerCase()) return
    accept()
  }, [accountEmail, preview.id, preview.email, accept])

  const signInToAccept = () => {
    rememberAcceptAfterSignIn(preview.id)
    navigate("/login", {
      state: {
        from: {
          pathname: "/accept-invitation",
          search: `?token=${encodeURIComponent(token)}`,
        },
        email: preview.email,
      },
    })
  }

  if (joined) return <InvitationJoined result={joined} preview={preview} />

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

// Loaded only for an invitation that creates an account without a password.
const EmailCodeFlow = React.lazy(() =>
  import("./email-code-flow").then((module) => ({
    default: module.EmailCodeFlow,
  }))
)

/**
 * New-account path: register with the invited email, then sign in and go on as
 * an accepted invitation would (OI-192). With a password, the sign-in uses the
 * password just chosen instead of asking for it a second time. Without one
 * (the deployment signs new accounts up without a password, OI-215), the
 * registration issues no session either, so a code is sent to the invited
 * address and the sign-in by code finishes it.
 *
 * The fields follow the deployment's sign-up options, a hint the server checks
 * again: a field it still requires comes back as a refusal. The form is keyed
 * on them by its caller, since a form keeps the resolver it was made with.
 */
function RegisterAndJoin({
  token,
  preview,
  askPassword,
  askName,
}: {
  token: string
  preview: InvitationPreview
  askPassword: boolean
  askName: boolean
}) {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { login } = useAuth()
  const { complete, challenge } = useLoginCompletion()
  const { policy } = usePasswordPolicy()
  const [codeSignIn, setCodeSignIn] = React.useState<{
    pending: PendingEmailCode
    launchUrl: string | null | undefined
    landing: string | undefined
  } | null>(null)

  const required = z.string().min(1, t("validation.required"))
  const schema = z
    .object({
      firstName: askName ? required : z.string(),
      lastName: askName ? required : z.string(),
      password: askPassword ? passwordSchema(policy) : z.string(),
      confirmPassword: askPassword ? required : z.string(),
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
    let registered: Schemas["RegisterWithInvitationResponse"]
    try {
      registered = await unwrap(
        api.POST("/api/v1/Invitations/{token}/register", {
          params: { path: { token } },
          body: {
            password: askPassword ? values.password : null,
            firstName: askName ? values.firstName : null,
            lastName: askName ? values.lastName : null,
          },
        })
      )
    } catch (error) {
      if (!askPassword || !applyPasswordServerErrors(form, "password", error)) {
        toast.error(getErrorMessage(error))
      }
      return
    }

    // Joined without the application role: said here too, since this path
    // goes straight on to the sign-in and the organization page.
    if (wasRoleDropped(registered.applicationRoleOutcome)) {
      const org = registered.organizationName || preview.organizationName || ""
      const app = registered.application?.name ?? preview.applicationName
      toast.warning(
        app
          ? t("auth.invitationRoleDropped", { org, app })
          : t("auth.invitationRoleDroppedNoApp", { org }),
        { duration: 15_000 }
      )
    }

    // The registration issues no session, by design: the ordinary sign-in
    // does, with the password just typed, and ends through the one completion
    // rule like every other sign-in.
    const landing = organizationPage(registered.organizationId)
    if (!askPassword) {
      try {
        setCodeSignIn({
          pending: await startEmailCode(preview.email ?? "", i18n.language),
          launchUrl: registered.launchUrl,
          landing,
        })
      } catch {
        // The account exists and has joined: only the sign-in is left to do.
        toast.success(t("auth.invitationRegisterSuccess"))
        navigate("/login", { state: { email: preview.email, from: landing } })
      }
      return
    }
    try {
      const result = await login(preview.email ?? "", values.password)
      if (result.status === "twoFactorRequired") {
        challenge(result.challengeToken, result.methods)
        return
      }
      toast.success(t("auth.invitationAccepted"))
      complete(result, { launchUrl: registered.launchUrl, from: landing })
    } catch {
      // The account exists and has joined: only the sign-in is left to do.
      toast.success(t("auth.invitationRegisterSuccess"))
      navigate("/login", { state: { email: preview.email, from: landing } })
    }
  }

  if (codeSignIn) {
    return (
      <React.Suspense
        fallback={
          <div className="flex justify-center">
            <Spinner className="text-muted-foreground" />
          </div>
        }
      >
        <EmailCodeFlow
          pending={codeSignIn.pending}
          staySignedIn={false}
          requireFullProof={false}
          completionOptions={{
            launchUrl: codeSignIn.launchUrl,
            from: codeSignIn.landing,
          }}
          onChangeAddress={() =>
            navigate("/login", {
              state: { email: preview.email, from: codeSignIn.landing },
            })
          }
          onPasswordRequired={(email) =>
            navigate("/login", {
              state: { email, from: codeSignIn.landing, method: "password" },
            })
          }
        />
      </React.Suspense>
    )
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
            {askName ? (
              <>
            <FormField
              control={form.control}
              name="firstName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("auth.invitationFirstName")}</FormLabel>
                  <FormControl>
                    <Input autoComplete="given-name" autoFocus {...field} />
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
                    <Input autoComplete="family-name" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
              </>
            ) : null}
            {askPassword ? (
              <>
            <PasswordField
              control={form.control}
              name="password"
              label={t("auth.password")}
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
              </>
            ) : null}
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
  const { status } = useAuth()
  // What a new account must set (OI-215). Unknown options ask for everything,
  // which the server can only narrow.
  const signInOptions = useSignInOptions()
  const askPassword = signInOptions?.passwordAtSignUp !== false
  const askName = signInOptions?.nameAtSignUp !== false

  // Tie the auto-accept note to the return from the sign-in it was written
  // for. Settled as signed out means this load is not that return, so the note
  // goes; while the session is still loading nothing is decided, since the
  // return from a sign-in can load that way before it settles as signed in.
  React.useEffect(() => {
    if (status === "unauthenticated") forgetAcceptAfterSignIn()
  }, [status])

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
    <RegisterAndJoin
      key={`${askPassword}-${askName}`}
      token={token}
      preview={preview}
      askPassword={askPassword}
      askName={askName}
    />
  )
}
