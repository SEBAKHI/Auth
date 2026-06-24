import { useTranslation } from "react-i18next"

import { StatusScreen } from "./status-screen"

export function ForbiddenPage() {
  const { t } = useTranslation()
  return (
    <StatusScreen
      code="403"
      title={t("errors.forbiddenTitle")}
      description={t("errors.forbiddenBody")}
    />
  )
}
