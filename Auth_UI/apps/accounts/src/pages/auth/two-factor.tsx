import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { TwoFactorVerifyPage } from "@astoom/auth/pages/two-factor-verify"

/**
 * Accounts-flavored two-factor step: same verification, plus the sign-up link
 * the end-user app offers everywhere else in the sign-in flow.
 */
export function AccountsTwoFactorPage() {
  const { t } = useTranslation()
  return (
    <TwoFactorVerifyPage
      footer={
        <span>
          {t("auth.noAccount")}{" "}
          <Link to="/register" className="underline-offset-4 hover:underline">
            {t("auth.signUp")}
          </Link>
        </span>
      }
    />
  )
}
