import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { useAppBranding } from "@authsystem/auth/use-app-branding"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"

/**
 * Asks whether to end the single sign-on session, after a relying party sent
 * the browser here.
 *
 * The confirmation is not politeness. Ending the session on the incoming GET
 * would mean any page anywhere could sign our users out by putting that URL in
 * an image tag, and we cannot tell a real relying party from that page: the
 * proof would be an `id_token_hint`, and this provider issues no id tokens. So
 * the person holding the browser decides.
 *
 * The application's name and logo come from the public-branding endpoint keyed
 * by client_id — never from the URL, which anyone can write.
 */
export function LogoutConfirmPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const clientId = params.get("client_id")
  const state = params.get("state")
  const branding = useAppBranding(clientId)
  const [pending, setPending] = React.useState(false)

  const appName = branding?.name ?? clientId ?? ""

  const signOut = React.useCallback(async () => {
    setPending(true)
    try {
      // Carries the SSO cookie and no bearer token: the browser arrived here
      // from another site and holds none. Same-site, so the Lax cookie rides
      // along — and a cross-site page could not have made this call at all.
      await api.POST("/api/v1/Auth/end-session", {})
    } finally {
      // Leave regardless of the outcome. A failure here means the session
      // survived, but the signed-out page is still where the user asked to go,
      // and the next authorize request discovers the truth either way.
      const query = new URLSearchParams()
      if (clientId) query.set("client_id", clientId)
      if (state) query.set("state", state)
      window.location.assign(`/signed-out?${query.toString()}`)
    }
  }, [clientId, state])

  return (
    <AuthLayout
      title={t("auth.signOutTitle", { name: appName })}
      appName={branding?.name}
      appLogoUrl={branding?.logoUrl ?? undefined}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">
          {t("auth.signOutBody", { name: appName })}
        </p>
        <Button onClick={signOut} disabled={pending} className="w-full">
          {pending ? <Spinner /> : t("auth.signOutConfirm")}
        </Button>
        <Button asChild variant="outline" className="w-full">
          <Link to="/profile">{t("auth.signOutCancel")}</Link>
        </Button>
      </div>
    </AuthLayout>
  )
}
