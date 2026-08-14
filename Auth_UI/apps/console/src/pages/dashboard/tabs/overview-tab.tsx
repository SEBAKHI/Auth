import { Activity, Building2, MonitorSmartphone, UserPlus } from "lucide-react"
import { useTranslation } from "react-i18next"

import { toNumber } from "@authsystem/api/helpers"

import { AttentionPanel } from "../attention-panel"
import { HeroHealthCard } from "../hero-health-card"
import { HygieneCard } from "../hygiene-card"
import { RecentActivityCard } from "../recent-activity-card"
import { StatTile } from "../stat-tile"
import {
  buildCountSeries,
  buildLoginSeries,
  rollupWeeklyCounts,
  rollupWeeklyLogins,
  successRate,
} from "../helpers"
import type { DashboardScope } from "./scope"
import {
  useAppActivity,
  useAuthStats,
  useCredentialStats,
  useOrganizationCount,
  useRecentActivity,
  useSessionStats,
  useUserStats,
} from "../use-dashboard-data"

/**
 * The default view: is anything wrong, and is the platform being used.
 *
 * Only the headline measures live here. Everything else moved to the tab that owns
 * its question, which is what turned a wall of ten stat cards plus fifteen charts
 * into something a reader can take in.
 */
export function OverviewTab({ scope }: { scope: DashboardScope }) {
  const { t } = useTranslation()
  const { days, granularity, timeZone, permissions } = scope
  const weekly = granularity === "weekly"

  const userStats = useUserStats(days, timeZone, permissions.users)
  const authStats = useAuthStats(days, timeZone, permissions.audit)
  const sessionStats = useSessionStats(days, permissions.audit)
  const appActivity = useAppActivity(days, permissions.apps)
  const recent = useRecentActivity(permissions.audit)
  const orgs = useOrganizationCount(permissions.allOrganizations)
  const credentialStats = useCredentialStats(
    permissions.apiKeys || permissions.webhookKeys
  )

  const auth = authStats.data
  const users = userStats.data

  const loginSeriesDaily = buildLoginSeries(
    auth?.loginsPerDay ?? [],
    days,
    timeZone
  )
  const loginSeries = weekly
    ? rollupWeeklyLogins(loginSeriesDaily)
    : loginSeriesDaily

  const dauDaily = buildCountSeries(auth?.activeUsersPerDay ?? [], days, timeZone)
  // Distinct users are NOT additive across days, so a weekly rollup would
  // over-count. The sparkline stays daily whatever the granularity.
  const signupsDaily = buildCountSeries(
    users?.signupsPerDay ?? [],
    days,
    timeZone
  )
  const signupSeries = weekly ? rollupWeeklyCounts(signupsDaily) : signupsDaily

  const windowSuccess = toNumber(auth?.windowSuccessCount)
  const windowFailure = toNumber(auth?.windowFailureCount)
  const rate = successRate(windowSuccess, windowFailure)
  const previousRate = successRate(
    toNumber(auth?.previousWindowSuccessCount),
    toNumber(auth?.previousWindowFailureCount)
  )
  const rateDelta =
    rate !== null && previousRate !== null
      ? Math.round((rate - previousRate) * 10) / 10
      : null

  const windowLabel = t("dashboard.windowLabel", { days })

  return (
    <div className="flex flex-col gap-4">
      <AttentionPanel
        authStats={auth}
        sessionStats={sessionStats.data}
        appActivity={appActivity.data}
        credentialStats={credentialStats.data}
        loading={authStats.isLoading || sessionStats.isLoading}
      />

      {permissions.audit ? (
        <HeroHealthCard
          rate={rate}
          rateDelta={rateDelta}
          success={windowSuccess}
          failure={windowFailure}
          series={loginSeries}
          loading={authStats.isLoading}
          description={windowLabel}
        />
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {permissions.audit ? (
          <StatTile
            title={t("dashboard.activeUsersWindow")}
            value={auth?.activeUsersInWindow ?? undefined}
            icon={Activity}
            loading={authStats.isLoading}
            series={dauDaily}
          />
        ) : null}
        {permissions.users ? (
          <StatTile
            title={t("dashboard.newUsers")}
            value={users?.newInWindow ?? undefined}
            icon={UserPlus}
            loading={userStats.isLoading}
            series={signupSeries}
          />
        ) : null}
        {permissions.audit ? (
          <StatTile
            title={t("dashboard.activeSessions")}
            value={sessionStats.data?.activeSessions ?? undefined}
            icon={MonitorSmartphone}
            loading={sessionStats.isLoading}
            hint={t("dashboard.staleSessionsHint", {
              count: toNumber(sessionStats.data?.staleOpenSessions),
            })}
          />
        ) : null}
        <StatTile
          title={t(
            permissions.allOrganizations
              ? "dashboard.totalOrganizations"
              : "dashboard.myOrganizations"
          )}
          value={toNumber(orgs.count)}
          icon={Building2}
          loading={orgs.isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        {permissions.users ? (
          <HygieneCard
            mfaEnabled={toNumber(users?.mfaEnabled)}
            activeUsers={toNumber(users?.activeUsers)}
            dormant30={toNumber(users?.dormantOver30Days)}
            dormant60={toNumber(users?.dormantOver60Days)}
            dormant90={toNumber(users?.dormantOver90Days)}
            neverLoggedIn={toNumber(users?.neverLoggedIn)}
            loading={userStats.isLoading}
          />
        ) : null}
        {permissions.audit ? (
          <div className="lg:col-span-2">
            <RecentActivityCard
              logs={recent.data?.logs ?? []}
              loading={recent.isLoading}
            />
          </div>
        ) : null}
      </div>
    </div>
  )
}
