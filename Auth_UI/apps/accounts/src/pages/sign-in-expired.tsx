import { useTranslation } from "react-i18next"
import { useSearchParams } from "react-router-dom"

import { useAppBranding } from "@authsystem/auth/use-app-branding"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { useBranding } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"

/**
 * Where a pending sign-in request ends when it can no longer be resumed: the
 * handle expired, was never issued, or names another application (OI-145). The
 * resume endpoint sends the browser here with `?client_id=` only when that code
 * is an active application's, and with nothing else from the request.
 *
 * Top-level and unguarded, like /signed-out: it renders whether or not this
 * app has a session. Nothing here can resume the request, so the page says to
 * start again from the application, where a person who already signed in will
 * find themselves signed in, and offers the way back when the application
 * publishes a home address.
 */
export function SignInExpiredPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const branding = useAppBranding(params.get("client_id"))
  const { name: platformName, isPending: brandingPending } = useBranding()
  const app = branding?.name ?? t("auth.theApplication")
  // A trust marker naming the wrong platform is worse than none at all.
  const securedBy = brandingPending
    ? null
    : t("auth.securedBy", { name: platformName })

  return (
    <AuthLayout
      title={t("auth.signInExpiredTitle")}
      appName={branding?.name}
      appLogoUrl={branding?.logoUrl ?? undefined}
      appLogoUrlDark={branding?.logoUrlDark ?? undefined}
      securedBy={branding ? securedBy : null}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">
          {t("auth.signInExpiredBody", { app })}
        </p>
        {branding?.homeUrl ? (
          <Button asChild className="w-full">
            <a href={branding.homeUrl}>{t("auth.backToApp", { app })}</a>
          </Button>
        ) : null}
      </div>
    </AuthLayout>
  )
}
