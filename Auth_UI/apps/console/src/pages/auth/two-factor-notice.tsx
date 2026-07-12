import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { Button } from "@astoom/ui/button"
import { AuthLayout } from "./auth-layout"

/**
 * The current API build issues a session at login and exposes 2FA only for
 * setup/enable/disable (there is no separate login-time 2FA verification
 * endpoint). When `requiresTwoFactor` is returned we surface this notice and let
 * the user continue with the issued session; 2FA is managed from the profile.
 */
export function TwoFactorNoticePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  return (
    <AuthLayout
      title={t("auth.twoFactorTitle")}
      subtitle={t("auth.twoFactorSubtitle")}
    >
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {t("auth.twoFactorLoginNotice")}
        </p>
        <Button
          className="w-full"
          onClick={() => navigate("/", { replace: true })}
        >
          {t("common.confirm")}
        </Button>
      </div>
    </AuthLayout>
  )
}
