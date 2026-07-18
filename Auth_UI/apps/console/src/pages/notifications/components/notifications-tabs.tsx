import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"

import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"

/**
 * Shared sub-navigation across the three notification list screens (templates,
 * layouts, delivery log), so they read as one section with a consistent, always
 * visible way to move between them — matching the tab pattern used elsewhere.
 */
export function NotificationsTabs() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()

  const active = pathname.startsWith("/notification-layouts")
    ? "layouts"
    : pathname.startsWith("/notification-outbox")
      ? "outbox"
      : "templates"

  const go = (value: string) => {
    if (value === "templates") navigate("/notification-templates")
    else if (value === "layouts") navigate("/notification-layouts")
    else navigate("/notification-outbox")
  }

  return (
    <Tabs value={active} onValueChange={go}>
      <TabsList>
        <TabsTrigger value="templates">{t("notifications.tabTemplates")}</TabsTrigger>
        <TabsTrigger value="layouts">{t("notifications.tabLayouts")}</TabsTrigger>
        <TabsTrigger value="outbox">{t("notifications.tabDeliveryLog")}</TabsTrigger>
      </TabsList>
    </Tabs>
  )
}
