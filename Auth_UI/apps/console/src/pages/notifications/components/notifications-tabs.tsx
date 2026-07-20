import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"

import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"

const TABS = [
  { value: "overview", path: "/notifications", labelKey: "notifications.tabOverview" },
  { value: "templates", path: "/notifications/templates", labelKey: "notifications.tabTemplates" },
  { value: "layouts", path: "/notifications/layouts", labelKey: "notifications.tabLayouts" },
  { value: "outbox", path: "/notifications/outbox", labelKey: "notifications.tabDeliveryLog" },
] as const

/**
 * Shared sub-navigation across the notification screens (overview, templates,
 * layouts, delivery log), so they read as one section with a consistent, always
 * visible way to move between them — matching the tab pattern used elsewhere.
 */
export function NotificationsTabs() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()

  // Longest match first so /notifications/templates does not read as the
  // overview's /notifications prefix.
  const active =
    [...TABS]
      .sort((a, b) => b.path.length - a.path.length)
      .find((tab) => pathname.startsWith(tab.path))?.value ?? "overview"

  return (
    <Tabs
      value={active}
      onValueChange={(value) => {
        const tab = TABS.find((item) => item.value === value)
        if (tab) navigate(tab.path)
      }}
    >
      <TabsList>
        {TABS.map((tab) => (
          <TabsTrigger key={tab.value} value={tab.value}>
            {t(tab.labelKey)}
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  )
}
