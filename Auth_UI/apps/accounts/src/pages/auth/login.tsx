import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { LoginPage } from "@astoom/auth/pages/login"

import { ExternalProviders } from "@/components/external-providers"

/** Accounts-flavored sign-in: external providers, sign-up link, end-user subtitle. */
export function AccountsLoginPage() {
  const { t } = useTranslation()
  return (
    <LoginPage
      subtitle={t("auth.signInSubtitleAccounts")}
      providers={<ExternalProviders />}
      footer={
        <div className="flex flex-col items-center gap-1">
          <span>
            {t("auth.noAccount")}{" "}
            <Link to="/register" className="underline-offset-4 hover:underline">
              {t("auth.signUp")}
            </Link>
          </span>
          <Link
            to="/delete-account"
            className="text-xs text-muted-foreground underline-offset-4 hover:underline"
          >
            {t("accountDeletion.deleteAccountLink")}
          </Link>
        </div>
      }
    />
  )
}
