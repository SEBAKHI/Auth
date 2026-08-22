import { useTranslation } from "react-i18next"
import { Link, useLocation } from "react-router-dom"

import { useAuth } from "@authsystem/auth/auth-context"
import { cn } from "@authsystem/ui/utils"
import { tabsListVariants, tabsTriggerVariants } from "@authsystem/ui/tabs"

import {
  NOTIFICATION_DESTINATIONS,
  visibleNotificationDestinations,
} from "@/lib/notification-destinations"

/**
 * Sub-navigation across the notification screens (overview, templates, layouts,
 * delivery log, policy), so they read as one section with a consistent, always
 * visible way to move between them.
 *
 * These are links, not tabs. Each one changes the URL, so it has to behave like
 * an address: middle-click or Ctrl/Cmd-click opens the section in a second tab,
 * "copy link address" yields something shareable, and the browser shows the
 * target on hover. Radix's Tabs cannot offer that, and it was actively lying
 * here: a tab announces `aria-controls` pointing at a panel, and this strip has
 * no panel - the routed page is the content - so every trigger carried a
 * dangling reference. Its default automatic activation also meant arrow-keying
 * along the strip fired a route change per keypress, instead of letting someone
 * move focus and then choose. The appearance is unchanged: the same class
 * strips the Tabs primitives use, applied to a nav and its links.
 */
export function NotificationsTabs() {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const { hasPermission } = useAuth()

  const visible = visibleNotificationDestinations(hasPermission)

  // Longest match first so /notifications/templates does not read as the
  // overview's /notifications prefix. Matched over every tab, not only the
  // visible ones, so a permitted deep link still highlights correctly.
  const active =
    [...NOTIFICATION_DESTINATIONS]
      .sort((a, b) => b.route.length - a.route.length)
      .find((tab) => pathname.startsWith(tab.route))?.id ?? "overview"

  return (
    <nav
      aria-label={t("notifications.sectionsNavLabel")}
      data-slot="tabs"
      data-orientation="horizontal"
      className="group/tabs flex gap-2 data-horizontal:flex-col"
    >
      <div data-slot="tabs-list" className={cn(tabsListVariants())}>
        {visible.map((tab) => {
          const isActive = tab.id === active
          return (
            <Link
              key={tab.id}
              to={tab.route}
              // `aria-current` is what tells assistive tech which section is
              // open; `data-active` is what draws the pill. The current section
              // stays a link so it can still be copied or opened in a new tab.
              aria-current={isActive ? "page" : undefined}
              data-active={isActive ? "" : undefined}
              className={cn(tabsTriggerVariants())}
            >
              {t(tab.tabLabelKey)}
            </Link>
          )
        })}
      </div>
    </nav>
  )
}
