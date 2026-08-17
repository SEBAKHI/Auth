import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"

import { useAuth } from "@authsystem/auth/auth-context"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"

import { PERMISSIONS } from "@/lib/constants"

/**
 * `permission` is set only where a tab needs one the section itself does not
 * imply. The privacy notice is such a case: it shares this navigation but not
 * its authority, so without the gate the tab advertised a destination that
 * bounced a notifications operator to /403.
 */
const TABS = [
  { value: "overview", path: "/notifications", labelKey: "notifications.tabOverview" },
  { value: "templates", path: "/notifications/templates", labelKey: "notifications.tabTemplates" },
  { value: "layouts", path: "/notifications/layouts", labelKey: "notifications.tabLayouts" },
  { value: "outbox", path: "/notifications/outbox", labelKey: "notifications.tabDeliveryLog" },
  {
    value: "policy",
    path: "/notifications/policy",
    labelKey: "notifications.tabPolicy",
    permission: PERMISSIONS.privacyPolicy.read,
  },
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
  const { hasPermission } = useAuth()

  const visible = TABS.filter(
    (tab) => !("permission" in tab) || hasPermission(tab.permission)
  )

  // Longest match first so /notifications/templates does not read as the
  // overview's /notifications prefix. Matched over every tab, not only the
  // visible ones, so a permitted deep link still highlights correctly.
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
        {visible.map((tab) => (
          <TabsTrigger key={tab.value} value={tab.value}>
            {t(tab.labelKey)}
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  )
}
