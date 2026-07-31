import { ScrollText } from "lucide-react"
import { useTranslation } from "react-i18next"

import { toNumber } from "@astoom/api/helpers"

import { CountSeriesCard } from "../count-series-card"
import { RankedBarCard } from "../ranked-bar-card"
import { RecentActivityCard } from "../recent-activity-card"
import { StatTile } from "../stat-tile"
import {
  buildCountSeries,
  pctDelta,
  rollupWeeklyCounts,
  topNWithOther,
} from "../helpers"
import type { DashboardScope } from "./scope"
import { useAuditStats, useRecentActivity } from "../use-dashboard-data"

/**
 * Audit events.
 *
 * Every number here is a server-side aggregate over the whole table. This view
 * previously derived its series and its "top actions" from a single 100-row page of
 * audit logs, so both under-reported the moment the window held more than 100
 * events — a sample presented with the same weight as a real total.
 */
export function AuditTab({ scope }: { scope: DashboardScope }) {
  const { t } = useTranslation()
  const { days, granularity, timeZone, permissions } = scope

  const auditStats = useAuditStats(days, timeZone, permissions.audit)
  const recent = useRecentActivity(permissions.audit, 12)

  const audit = auditStats.data
  const refetching = auditStats.isFetching && !auditStats.isLoading

  const eventsDaily = buildCountSeries(audit?.eventsPerDay ?? [], days, timeZone)
  const eventSeries =
    granularity === "weekly" ? rollupWeeklyCounts(eventsDaily) : eventsDaily

  const total = toNumber(audit?.totalInWindow)
  const previous = toNumber(audit?.previousWindowTotal)

  const topActions = topNWithOther(
    (audit?.topActions ?? []).map((row) => ({
      label: row.reason ?? t("common.unknown"),
      value: toNumber(row.count),
    })),
    8,
    (rest) => ({
      label: t("dashboard.other"),
      value: rest.reduce((sum, row) => sum + row.value, 0),
    })
  )

  const byEntity = topNWithOther(
    (audit?.byEntityType ?? []).map((row) => ({
      label:
        row.reason === "unknown" ? t("common.unknown") : (row.reason ?? "—"),
      value: toNumber(row.count),
    })),
    8,
    (rest) => ({
      label: t("dashboard.other"),
      value: rest.reduce((sum, row) => sum + row.value, 0),
    })
  )

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          title={t("dashboard.events")}
          value={total}
          icon={ScrollText}
          loading={auditStats.isLoading}
          delta={pctDelta(total, previous)}
          series={eventsDaily}
          className="sm:col-span-2"
        />
      </div>

      <CountSeriesCard
        title={t("dashboard.eventsOverTime")}
        description={t("dashboard.eventsOverTimeSubtitle")}
        seriesLabel={t("dashboard.events")}
        data={eventSeries}
        loading={auditStats.isLoading}
        refetching={refetching}
        exportName="audit-events"
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <RankedBarCard
          title={t("dashboard.topActions")}
          description={t("dashboard.topActionsSubtitle")}
          seriesLabel={t("dashboard.events")}
          data={topActions}
          loading={auditStats.isLoading}
          refetching={refetching}
          exportName="audit-top-actions"
        />
        <RankedBarCard
          title={t("dashboard.eventsByEntity")}
          description={t("dashboard.eventsByEntitySubtitle")}
          seriesLabel={t("dashboard.events")}
          data={byEntity}
          loading={auditStats.isLoading}
          refetching={refetching}
          exportName="audit-events-by-entity"
        />
      </div>

      <RecentActivityCard logs={recent.data?.logs ?? []} loading={recent.isLoading} />
    </div>
  )
}
