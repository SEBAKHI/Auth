import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { useLoginCompletion } from "@authsystem/auth/login-completion"
import { TwoFactorVerifyPage } from "@authsystem/auth/pages/two-factor-verify"

/**
 * Accounts-flavored two-factor step: same verification, plus the sign-up link
 * the end-user app offers everywhere else in the sign-in flow.
 */
export function AccountsTwoFactorPage() {
  const { t } = useTranslation()
  // This screen is entered by router state, never with a query string, so the
  // pending authorize request is read from the same place the verify page
  // reads it and put back into the query for the sign-up screens, which carry
  // it that way. Built from the validated value, not the raw state.
  const { returnTo } = useLoginCompletion({ resumePending: true })
  const search = returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : ""
  return (
    <TwoFactorVerifyPage
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
    />
  )
}
