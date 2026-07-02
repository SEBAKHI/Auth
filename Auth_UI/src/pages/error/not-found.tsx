import { useTranslation } from "react-i18next"

import { StatusScreen } from "./status-screen"

export function NotFoundPage() {
  const { t } = useTranslation()
  return (
    <StatusScreen
      code="404"
      title={t("errors.notFoundTitle")}
      description={t("errors.notFoundBody")}
    />
  )
}
