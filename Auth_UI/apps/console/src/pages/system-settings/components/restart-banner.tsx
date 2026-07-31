import { TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"

/** Page-top banner shown while a restart-required change is not live yet. */
export function RestartBanner() {
  const { t } = useTranslation()
  return (
    <Alert>
      <TriangleAlert />
      <AlertTitle>{t("systemSettings.restartBannerTitle")}</AlertTitle>
      <AlertDescription>{t("systemSettings.restartBannerBody")}</AlertDescription>
    </Alert>
  )
}

/** Banner shown when the last database-overrides load failed. */
export function DbUnavailableBanner() {
  const { t } = useTranslation()
  return (
    <Alert variant="destructive">
      <TriangleAlert />
      <AlertTitle>{t("systemSettings.dbUnavailableTitle")}</AlertTitle>
      <AlertDescription>{t("systemSettings.dbUnavailableBody")}</AlertDescription>
    </Alert>
  )
}
