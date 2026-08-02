import { useTranslation } from "react-i18next"

import { toNumber } from "@authsystem/api/helpers"

import { ActivityTableCard } from "../activity-table-card"
import type { ActivityRow } from "../activity-table-card"
import { EnablementMatrixCard } from "../enablement-matrix-card"
import type { DashboardScope } from "./scope"
import { useAppActivity, useAuthStats } from "../use-dashboard-data"

/** Where sign-ins happen, and which organizations have which applications. */
export function AppsTab({ scope }: { scope: DashboardScope }) {
  const { t } = useTranslation()
  const { days, timeZone, permissions } = scope

  const appActivity = useAppActivity(days, permissions.apps)
  const authStats = useAuthStats(days, timeZone, permissions.audit)

  const auth = authStats.data
  const activity = appActivity.data

  // Sign-in outcomes come from auth-stats, per-app users and sessions from
  // app-activity; joined here by application id so one row carries both.
  const outcomesByApp = new Map(
    (auth?.loginsByApplication ?? []).map((row) => [
      row.applicationId ?? "unattributed",
      row,
    ])
  )

  const applications: ActivityRow[] = (activity?.applications ?? []).map(
    (row) => {
      const key = row.applicationId ?? "unattributed"
      const outcome = outcomesByApp.get(key)
      return {
        key,
        label: row.applicationName ?? t("dashboard.unattributed"),
        success: toNumber(outcome?.successCount ?? row.successfulLogins),
        failure: toNumber(outcome?.failureCount),
        people: toNumber(row.distinctUsers),
        sessions: toNumber(row.activeSessions),
        inactive: row.isActive === false,
      }
    }
  )

  const organizations: ActivityRow[] = (auth?.loginsByOrganization ?? []).map(
    (row) => ({
      key: row.organizationId ?? "unattributed",
      label: row.organizationName ?? t("dashboard.unattributed"),
      success: toNumber(row.successCount),
      failure: toNumber(row.failureCount),
    })
  )

  return (
    <div className="flex flex-col gap-4">
      <ActivityTableCard
        applications={applications}
        organizations={organizations}
        loading={appActivity.isLoading || authStats.isLoading}
        refetching={
          (appActivity.isFetching && !appActivity.isLoading) ||
          (authStats.isFetching && !authStats.isLoading)
        }
        organizationNote={t("dashboard.whereSignInsOrgNote")}
      />
      <EnablementMatrixCard
        data={activity?.organizationApplications ?? []}
        loading={appActivity.isLoading}
      />
    </div>
  )
}
