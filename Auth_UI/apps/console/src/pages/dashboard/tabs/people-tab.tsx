import { useTranslation } from "react-i18next"

import { toNumber } from "@authsystem/api/helpers"
import { userStatusMeta } from "@authsystem/ui/format"

import { CountSeriesCard } from "../count-series-card"
import { FunnelCard } from "../funnel-card"
import { RankedBarCard } from "../ranked-bar-card"
import { ShareBarCard } from "../share-bar-card"
import type { ShareSlice } from "../share-bar-card"
import { buildCountSeries, rollupWeeklyCounts } from "../helpers"
import type { DashboardScope } from "./scope"
import { useUserStats } from "../use-dashboard-data"

/** Status order for the share bar — the classes are ordinal, so the order is fixed. */
const STATUS_ORDER = ["active", "pending", "locked", "inactive", "unknown"]

/** Who the users are: status mix, activation, growth, and where they belong. */
export function PeopleTab({ scope }: { scope: DashboardScope }) {
  const { t } = useTranslation()
  const { days, granularity, timeZone, permissions } = scope

  const userStats = useUserStats(days, timeZone, permissions.users)
  const users = userStats.data
  const refetching = userStats.isFetching && !userStats.isLoading

  const signupsDaily = buildCountSeries(
    users?.signupsPerDay ?? [],
    days,
    timeZone
  )
  const signupSeries =
    granularity === "weekly" ? rollupWeeklyCounts(signupsDaily) : signupsDaily

  const statusSlices: ShareSlice[] = (users?.byStatus ?? [])
    .map((row) => {
      const meta = userStatusMeta(row.status)
      return {
        key: meta.key,
        label: t(`common.${meta.key}`),
        value: toNumber(row.count),
      }
    })
    .sort(
      (a, b) =>
        (STATUS_ORDER.indexOf(a.key) + 1 || STATUS_ORDER.length) -
        (STATUS_ORDER.indexOf(b.key) + 1 || STATUS_ORDER.length)
    )

  const orgMembers = (users?.usersByOrganization ?? []).map((row) => ({
    label: row.organizationName ?? t("common.unknown"),
    value: toNumber(row.count),
  }))
  const listed = orgMembers.reduce((sum, row) => sum + row.value, 0)
  const remainder = toNumber(users?.totalActiveMemberships) - listed
  const usersByOrg =
    remainder > 0
      ? [...orgMembers, { label: t("dashboard.other"), value: remainder }]
      : orgMembers

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 lg:grid-cols-2">
        <ShareBarCard
          title={t("dashboard.usersByStatus")}
          description={t("dashboard.usersByStatusSubtitle")}
          data={statusSlices}
          loading={userStats.isLoading}
          refetching={refetching}
          exportName="users-by-status"
        />
        <FunnelCard
          created={toNumber(users?.cohortCreated)}
          confirmed={toNumber(users?.cohortEmailConfirmed)}
          loggedIn={toNumber(users?.cohortLoggedIn)}
          loading={userStats.isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <CountSeriesCard
          title={t("dashboard.signups")}
          description={t("dashboard.signupsSubtitle")}
          seriesLabel={t("dashboard.signupsSeries")}
          data={signupSeries}
          variant="bars"
          loading={userStats.isLoading}
          refetching={refetching}
          exportName="signups"
        />
        <RankedBarCard
          title={t("dashboard.usersByOrganization")}
          description={t("dashboard.usersByOrganizationSubtitle")}
          seriesLabel={t("dashboard.members")}
          data={usersByOrg}
          loading={userStats.isLoading}
          refetching={refetching}
          exportName="members-by-organization"
        />
      </div>
    </div>
  )
}
