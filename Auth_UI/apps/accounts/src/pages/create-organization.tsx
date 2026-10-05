import { zodResolver } from "@hookform/resolvers/zod"
import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { api } from "@authsystem/api/client"
import {
  getErrorFeedback,
  getErrorMessage,
  getErrorStatus,
} from "@authsystem/api/errors"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import { getReturnToClientId, getValidReturnTo } from "@authsystem/auth/return-to"
import { useAppBranding } from "@authsystem/auth/use-app-branding"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { useBranding } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import { FieldDescription, FieldGroup } from "@authsystem/ui/field"
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
import { Separator } from "@authsystem/ui/separator"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Spinner } from "@authsystem/ui/spinner"

import { signInPath, withoutOrganizationRequest } from "./create-organization-flow"

/** The longest organization name the API accepts. */
const NAME_MAX_LENGTH = 200

/**
 * An application asked, through authorize, that the signed-in user own an
 * organization set up for it, and they do not yet. This page lets them name
 * one (or pick one they own), then sends the browser back to the same
 * authorize request, which now issues the code.
 *
 * Top-level and self-guarded: the person here holds a single sign-on session,
 * which is what the two calls below authenticate with (the IdP cookie, never
 * this app's bearer), and may or may not hold a session in this app. A visitor
 * whose SSO session is gone is sent to sign in, with the same returnTo.
 */
export function CreateOrganizationPage() {
  const { search } = useLocation()
  const returnTo = React.useMemo(() => getValidReturnTo(search), [search])
  const clientId = getReturnToClientId(returnTo)

  if (!returnTo || !clientId) return <NothingWaiting />

  return <OrganizationSetup returnTo={returnTo} clientId={clientId} />
}

/**
 * Reached without a valid pending authorize request: nothing to come back to,
 * so the page points at the organizations the user manages instead of a form
 * whose result would go nowhere.
 */
function NothingWaiting() {
  const { t } = useTranslation()

  return (
    <AuthLayout title={t("auth.createOrganizationTitle")}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">
          {t("auth.createOrganizationNoReturn")}
        </p>
        <Button asChild className="w-full">
          <Link to="/organizations">{t("auth.goToOrganizations")}</Link>
        </Button>
      </div>
    </AuthLayout>
  )
}

function OrganizationSetup({
  returnTo,
  clientId,
}: {
  returnTo: string
  clientId: string
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const branding = useAppBranding(clientId)
  const { name: platformName, isPending: brandingPending } = useBranding()
  const app = branding?.name ?? t("auth.theApplication")
  const securedBy = brandingPending
    ? null
    : t("auth.securedBy", { name: platformName })

  // A latch, not the pending flag: the flag reaches the button a frame after
  // the click, and a second click inside that frame would post again.
  const submitting = React.useRef(false)
  const [pendingId, setPendingId] = React.useState<string | null>(null)

  // A page restored from the back/forward cache keeps the latch it left with,
  // which would leave every button disabled until a reload.
  React.useEffect(() => {
    const restored = (event: PageTransitionEvent) => {
      if (!event.persisted) return
      submitting.current = false
      setPendingId(null)
    }
    window.addEventListener("pageshow", restored)
    return () => window.removeEventListener("pageshow", restored)
  }, [])

  const stateQuery = useQuery({
    queryKey: ["organization-setup", clientId],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Auth/organization-setup", {
          params: { query: { clientId } },
        })
      ),
    retry: false,
  })

  const schema = z.object({
    // The length cap is the input's own maxLength; the API refuses a longer
    // name with its own code either way.
    name: z.string().trim().min(1, t("validation.required")),
  })
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: "" },
  })

  const setUp = async (
    body: { name: string } | { organizationId: string },
    pending: string
  ) => {
    if (submitting.current) return
    submitting.current = true
    setPendingId(pending)
    try {
      await unwrap(
        api.POST("/api/v1/Auth/organization-setup", {
          body: { clientId, ...body },
        })
      )
      // A full navigation, not a router one: the authorize endpoint is on the
      // API origin and needs the SSO cookie to ride along.
      window.location.assign(returnTo)
    } catch (error) {
      submitting.current = false
      setPendingId(null)
      if (getErrorStatus(error) === 401) {
        navigate(signInPath(returnTo), { replace: true })
        return
      }
      toast.error(getErrorMessage(error))
      // The limit or the organizations offered may have changed under us (a
      // second tab, an administrator): show what is true now.
      void stateQuery.refetch()
    }
  }

  const continueWithout = (
    <div className="flex flex-col gap-2">
      <Button
        variant="outline"
        className="w-full"
        onClick={() => window.location.assign(withoutOrganizationRequest(returnTo))}
      >
        {t("auth.continueWithoutOrganization")}
      </Button>
      <FieldDescription className="text-center">
        {t("auth.continueWithoutOrganizationHint", { app })}
      </FieldDescription>
    </div>
  )

  if (stateQuery.isError && getErrorStatus(stateQuery.error) === 401) {
    return <Navigate replace to={signInPath(returnTo)} />
  }

  const state = stateQuery.data
  const owned = state?.ownedOrganizations ?? []

  return (
    <AuthLayout
      title={t("auth.createOrganizationTitle")}
      subtitle={t("auth.createOrganizationSubtitle", { app })}
      appName={branding?.name}
      appLogoUrl={branding?.logoUrl}
      securedBy={securedBy}
    >
      <div className="flex flex-col gap-6">
        {stateQuery.isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : stateQuery.isError ? (
          // Refused by policy (the application cannot create organizations, an
          // unconfirmed address) or broken: say which, and keep the way back.
          <Alert variant="destructive">
            <AlertDescription>
              {getErrorFeedback(stateQuery.error).description}
            </AlertDescription>
          </Alert>
        ) : (
          <>
            <p className="text-sm text-muted-foreground">
              {t("auth.createOrganizationSignedInAs", { email: state?.email })}
            </p>
            {state && !state.emailConfirmed ? (
              // No organization before the address is proven. An account
              // linked by Google or Apple, or created by an administrator, can
              // arrive here signed in and unconfirmed: offer the confirmation,
              // which signs in again and comes back through the same request.
              <div className="flex flex-col gap-3">
                <Alert>
                  <AlertDescription>
                    {t("auth.createOrganizationConfirmEmail", { app })}
                  </AlertDescription>
                </Alert>
                <Button
                  className="w-full"
                  onClick={() =>
                    navigate(
                      {
                        pathname: "/verify-email",
                        search: `?returnTo=${encodeURIComponent(returnTo)}`,
                      },
                      { state: { email: state.email } }
                    )
                  }
                >
                  {t("auth.verifyEmailTitle")}
                </Button>
              </div>
            ) : state?.canCreate ? (
              <Form {...form}>
                <form
                  // handleSubmit runs inside the event, so the latch in setUp
                  // is read when the user submits, never during render.
                  onSubmit={(event) =>
                    void form.handleSubmit((values) =>
                      setUp({ name: values.name }, "create")
                    )(event)
                  }
                >
                  <FieldGroup>
                    <FormField
                      control={form.control}
                      name="name"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>{t("auth.organizationName")}</FormLabel>
                          <FormControl>
                            <Input
                              autoComplete="organization"
                              autoFocus
                              maxLength={NAME_MAX_LENGTH}
                              {...field}
                            />
                          </FormControl>
                          <FormDescription>
                            {t("auth.organizationNameHint", { app })}
                          </FormDescription>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <Button
                      type="submit"
                      className="w-full"
                      disabled={pendingId !== null}
                    >
                      {pendingId === "create" ? (
                        <>
                          <Spinner data-icon="inline-start" />
                          {t("auth.createOrganizationCreating")}
                        </>
                      ) : (
                        t("auth.createOrganizationSubmit")
                      )}
                    </Button>
                  </FieldGroup>
                </form>
              </Form>
            ) : (
              <Alert>
                <AlertDescription>
                  {t("auth.createOrganizationLimitReached", {
                    limit: toNumber(state?.limit),
                  })}
                </AlertDescription>
              </Alert>
            )}
            {owned.length > 0 ? (
              <div className="flex flex-col gap-3">
                <Separator />
                <p className="text-sm font-medium">
                  {t("auth.useOwnedOrganizationTitle")}
                </p>
                <FieldDescription>
                  {t("auth.useOwnedOrganizationHint", { app })}
                </FieldDescription>
                {owned.map((organization) => (
                  <Button
                    key={organization.id}
                    variant="secondary"
                    className="w-full"
                    disabled={pendingId !== null}
                    onClick={() =>
                      setUp({ organizationId: organization.id }, organization.id)
                    }
                  >
                    {pendingId === organization.id ? (
                      <Spinner data-icon="inline-start" />
                    ) : null}
                    {t("auth.useOwnedOrganization", { name: organization.name })}
                  </Button>
                ))}
              </div>
            ) : null}
          </>
        )}
        {continueWithout}
      </div>
    </AuthLayout>
  )
}
