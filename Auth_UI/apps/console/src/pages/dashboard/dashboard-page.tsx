import * as React from "react"
import { useTranslation } from "react-i18next"

import { PageHeader } from "@authsystem/ui/common/page-header"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { useAuth } from "@authsystem/auth/auth-context"
import { getTimeZoneOffsetLabel } from "@authsystem/i18n/timezone"
import { PERMISSIONS } from "@/lib/constants"

import { WindowFilter } from "./window-filter"
import { useDashboardWindow } from "./use-dashboard-window"
import {
  AppsTab,
  AuditTab,
  OverviewTab,
  PeopleTab,
  SecurityTab,
  TabFallback,
} from "./tabs/lazy-tabs"
import type { DashboardScope } from "./tabs/scope"

/**
 * The console dashboard.
 *
 * One window scopes everything (see `WindowFilter`), and the deep-dive sections are
 * tabs rather than one long scroll — each tab fetches only its own aggregates, so
 * the default view issues a handful of requests instead of ten.
 *
 * Tab and window both live in the URL, so a view can be linked and the Back button
 * behaves.
 */
export function DashboardPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const { days, granularity, tab, timeZone, update } = useDashboardWindow()

  const permissions = {
    users: hasPermission(PERMISSIONS.users.read),
    apps: hasPermission(PERMISSIONS.applications.read),
    roles: hasPermission(PERMISSIONS.roles.read),
    audit: hasPermission(PERMISSIONS.auditLogs.read),
    allOrganizations: hasPermission(PERMISSIONS.organizations.read),
    apiKeys: hasPermission(PERMISSIONS.apiKeys.read),
    webhookKeys: hasPermission(PERMISSIONS.webhookKeys.read),
  }

  const scope: DashboardScope = { days, granularity, timeZone, permissions }

  const offset = getTimeZoneOffsetLabel(timeZone)
  // Bidi isolates: an offset like "UTC+03:00" next to Arabic text otherwise
  // reorders into nonsense.
  const timeZoneLabel = `⁦${offset ? `${timeZone} (${offset})` : timeZone}⁩`

  // A tab whose data the viewer cannot read is not offered at all.
  const tabs = [
    { value: "overview", label: t("dashboard.tabOverview"), visible: true },
    {
      value: "security",
      label: t("dashboard.tabSecurity"),
      visible: permissions.audit,
    },
    {
      value: "people",
      label: t("dashboard.tabPeople"),
      visible: permissions.users,
    },
    {
      value: "apps",
      label: t("dashboard.tabApplications"),
      visible: permissions.apps,
    },
    {
      value: "audit",
      label: t("dashboard.tabAudit"),
      visible: permissions.audit,
    },
  ].filter((entry) => entry.visible)

  const active = tabs.some((entry) => entry.value === tab) ? tab : "overview"

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("dashboard.title")}
        description={t("dashboard.subtitleWindow", {
          days,
          timeZone: timeZoneLabel,
        })}
      />

      <WindowFilter
        days={days}
        granularity={granularity}
        onChange={(next) => update(next)}
      />

      <Tabs value={active} onValueChange={(value) => update({ tab: value })}>
        <TabsList>
          {tabs.map((entry) => (
            <TabsTrigger key={entry.value} value={entry.value}>
              {entry.label}
            </TabsTrigger>
          ))}
        </TabsList>

        {/* Only the active tab is mounted, so an inactive tab issues no requests. */}
        <React.Suspense fallback={<TabFallback />}>
          <TabsContent value="overview">
            {active === "overview" ? <OverviewTab scope={scope} /> : null}
          </TabsContent>
          <TabsContent value="security">
            {active === "security" ? <SecurityTab scope={scope} /> : null}
          </TabsContent>
          <TabsContent value="people">
            {active === "people" ? <PeopleTab scope={scope} /> : null}
          </TabsContent>
          <TabsContent value="apps">
            {active === "apps" ? <AppsTab scope={scope} /> : null}
          </TabsContent>
          <TabsContent value="audit">
            {active === "audit" ? <AuditTab scope={scope} /> : null}
          </TabsContent>
        </React.Suspense>
      </Tabs>
    </div>
  )
}
