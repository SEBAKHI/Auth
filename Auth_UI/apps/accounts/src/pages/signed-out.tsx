import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"

import { useFlowBranding } from "@authsystem/auth/flow-branding"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Button } from "@authsystem/ui/button"

/**
 * Where a relying party's logout ends: the single sign-on session is gone.
 *
 * Top-level and unguarded, like /deletion-scheduled — it must render while
 * fully signed out. It is also the landing place when there was nothing to end
 * at all: a second click, a refresh, or a tab left open past the session's
 * lifetime all arrive here, and all of them are already in the state they
 * asked for, so none of them should see an error.
 *
 * The application its `client_id` names gives the page its header, its
 * appearance and its design, from the one hook every such page reads.
 */
export function SignedOutPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const { layout } = useFlowBranding({ clientId: params.get("client_id") })

  return (
    <AuthLayout title={t("auth.signedOutTitle")} {...layout}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-muted-foreground">
          {t("auth.signedOutBody")}
        </p>
        <Button asChild variant="outline" className="w-full">
          <Link to="/login">{t("auth.backToSignIn")}</Link>
        </Button>
      </div>
    </AuthLayout>
  )
}
