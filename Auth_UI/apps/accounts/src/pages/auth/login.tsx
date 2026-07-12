import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { LoginPage } from "@astoom/auth/pages/login"

import { GoogleSignIn } from "@/components/google-sign-in"

/** Accounts-flavored sign-in: Google option, sign-up link, end-user subtitle. */
export function AccountsLoginPage() {
  const { t } = useTranslation()
  return (
    <LoginPage
      subtitle={t("auth.signInSubtitleAccounts")}
      providers={<GoogleSignIn />}
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
