import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

/** "Don't have an account? Sign up" link under the sign-in card. */
export function LoginFooter() {
  const { t } = useTranslation()
  return (
    <span>
      {t("auth.noAccount")}{" "}
      <Link to="/register" className="underline-offset-4 hover:underline">
        {t("auth.signUp")}
      </Link>
    </span>
  )
}
