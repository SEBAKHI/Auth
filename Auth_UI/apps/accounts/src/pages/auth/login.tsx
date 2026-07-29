import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { LoginPage } from "@astoom/auth/pages/login"

import { ExternalProviders } from "@/components/external-providers"

/**
 * Accounts-flavored sign-in: external providers, sign-up link, end-user subtitle.
 *
 * Deliberately does NOT link the public /delete-account wizard: a terminal,
 * destructive action does not belong on the authentication surface, where the
 * dominant intent is "I can't get in" (a forgotten password, not erasure).
 * The page stays publicly reachable by URL, which is what the compliance
 * surfaces consume — see the plan's rollout step 10 (privacy-policy retention
 * disclosure + the store listing's data-deletion URL field).
 */
export function AccountsLoginPage() {
  const { t } = useTranslation()
  return (
    <LoginPage
      subtitle={t("auth.signInSubtitleAccounts")}
      providers={<ExternalProviders />}
      footer={
        <span>
          {t("auth.noAccount")}{" "}
          <Link to="/register" className="underline-offset-4 hover:underline">
            {t("auth.signUp")}
          </Link>
        </span>
      }
      pageFooter={
        <Link to="/privacy" className="underline-offset-4 hover:underline">
          {t("auth.privacyPolicy")}
        </Link>
      }
    />
  )
}
