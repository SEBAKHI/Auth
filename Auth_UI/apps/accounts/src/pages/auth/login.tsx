import { useTranslation } from "react-i18next"
import { Link, useLocation } from "react-router-dom"

import { privacyPolicyUrl } from "@authsystem/api/env"
import { LoginPage } from "@authsystem/auth/pages/login"

import { ExternalProviders } from "@authsystem/auth/external/external-providers"

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
  const { t, i18n } = useTranslation()
  // A pending authorize request rides in the query string; the sign-up link
  // carries it so a relying party's visitor who creates an account still
  // returns to that party at the end.
  const { search } = useLocation()
  return (
    <LoginPage
      subtitle={t("auth.signInSubtitleAccounts")}
      recoveryPath="/account-recovery"
      providers={<ExternalProviders recoveryPath="/account-recovery" />}
      footer={
        <span>
          {t("auth.noAccount")}{" "}
          <Link
            to={{ pathname: "/register", search }}
            className="underline-offset-4 hover:underline"
          >
            {t("auth.signUp")}
          </Link>
        </span>
      }
      pageFooter={
        <a
          href={privacyPolicyUrl(i18n.language)}
          className="underline-offset-4 hover:underline"
        >
          {t("auth.privacyPolicy")}
        </a>
      }
    />
  )
}
