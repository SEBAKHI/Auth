import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { API_BASE_URL } from "@authsystem/api/env"
import { useAuth } from "@authsystem/auth/auth-context"
import { useFlowBranding } from "@authsystem/auth/flow-branding"
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
 * by client_id — never from the URL, which anyone can write. With them come its
 * appearance and its design, from the one hook every such page reads.
 *
 * When the application registered where to come back to after signing out
 * (`post_logout_redirect_uri`), the URL names that address by its hash, in
 * `plr`, and never carries it: a web application firewall may refuse a page
 * whose query holds a whole address. Only once the server has confirmed the
 * session ended does the browser go back, through the end-session endpoint,
 * which resolves the hash against the application's own list. Anything short of
 * that confirmation lands on the signed-out page, so the application is never
 * told the session ended when it did not; Cancel never goes back.
 *
 * If the confirmation answered 204 but ended nothing, the server sends the
 * browser back here WITHOUT the hash, so the next confirmation ends on the
 * signed-out page instead of going round again. That rule is the server's; the
 * page keeps no memory of earlier sign-outs.
 */
export function LogoutConfirmPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const clientId = params.get("client_id")
  const state = params.get("state")
  const returnHash = readReturnHash(params)
  const { layout } = useFlowBranding({ clientId })
  const { status, logout } = useAuth()
  const [pending, setPending] = React.useState(false)

  const appName = layout.appName

  const signOut = React.useCallback(async () => {
    setPending(true)
    // Leave regardless of the outcome. A failure means a session survived, but
    // the signed-out page is still where the user asked to go, and the next
    // authorize request discovers the truth either way.
    let destination = signedOutPath(clientId, state)
    try {
      // Carries the SSO cookie and no bearer token: the browser arrived here
      // from another site and holds none. Same-site, so the Lax cookie rides
      // along — and a cross-site page could not have made this call at all.
      const { response } = await api.POST("/api/v1/Auth/end-session", {})

      // And this account app's OWN session, when there is one. Ending only the
      // SSO session would leave the visitor signed in right here while being
      // told they had been signed out — and would bounce them off /login on the
      // next screen, straight back into the profile they just left.
      if (status === "authenticated") {
        await logout()
      }

      // Back to the application only on the server's word that the session
      // ended. The end-session endpoint checks that again before it redirects.
      if (response.status === 204 && clientId && returnHash) {
        destination = endSessionReturnUrl(clientId, state, returnHash)
      }
    } catch (error) {
      // Reported, not hidden: the browser still leaves for the signed-out page.
      console.error("[auth] sign-out confirmation failed", error)
    } finally {
      window.location.assign(destination)
    }
  }, [clientId, state, returnHash, status, logout])

  return (
    <AuthLayout
      title={
        appName
          ? t("auth.signOutTitle", { name: appName })
          : t("auth.signOutTitleNoApp")
      }
      {...layout}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">{t("auth.signOutBody")}</p>
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

/**
 * The internal parameter the end-session endpoint names a registered return
 * address by (`PostLogoutReturnHash.Parameter` on the server).
 */
const RETURN_HASH_PARAM = "plr"

/** The lowercase hex SHA-256 the server writes, and nothing else. */
const RETURN_HASH_RE = /^[0-9a-f]{64}$/

function readReturnHash(params: URLSearchParams): string | null {
  const value = params.get(RETURN_HASH_PARAM)
  return value && RETURN_HASH_RE.test(value) ? value : null
}

function signedOutPath(clientId: string | null, state: string | null): string {
  const query = new URLSearchParams()
  if (clientId) query.set("client_id", clientId)
  if (state) query.set("state", state)

  const search = query.toString()
  return search ? `/signed-out?${search}` : "/signed-out"
}

/**
 * The end-session endpoint again, now with no session left: it sends the
 * browser to the address registered for `clientId` whose hash this is, with
 * `state`. The address itself is never on this side.
 */
function endSessionReturnUrl(
  clientId: string,
  state: string | null,
  returnHash: string
): string {
  const query = new URLSearchParams({ client_id: clientId })
  if (state) query.set("state", state)
  query.set(RETURN_HASH_PARAM, returnHash)
  return `${API_BASE_URL}/api/v1/auth/end-session?${query.toString()}`
}
