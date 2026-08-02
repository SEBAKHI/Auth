import { useTranslation } from "react-i18next"

import { toNumber } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

import { CountSeriesCard } from "../count-series-card"
import { IpTableCard } from "../ip-table-card"
import { RankedBarCard } from "../ranked-bar-card"
import { StatTile } from "../stat-tile"
import { buildCountSeries, rollupWeeklyCounts, topNWithOther } from "../helpers"
import type { DashboardScope } from "./scope"
import { useAuthStats, useSessionStats } from "../use-dashboard-data"
import { KeySquare, Lock, Timer } from "lucide-react"

/** Sign-in failures, lockouts, and session/token hygiene. */
export function SecurityTab({ scope }: { scope: DashboardScope }) {
  const { t } = useTranslation()
  const { days, granularity, timeZone, permissions } = scope

  const authStats = useAuthStats(days, timeZone, permissions.audit)
  const sessionStats = useSessionStats(days, permissions.audit)

  const auth = authStats.data
  const session = sessionStats.data

  const failuresDaily = buildCountSeries(
    (auth?.loginsPerDay ?? []).map((row) => ({
      date: row.date,
      count: row.failureCount,
    })),
    days,
    timeZone
  )
  const failureSeries =
    granularity === "weekly" ? rollupWeeklyCounts(failuresDaily) : failuresDaily

  const failureReasons = topNWithOther(
    (auth?.failureReasons ?? []).map((row) => ({
      label: row.reason ?? t("common.unknown"),
      value: toNumber(row.count),
    })),
    8,
    (rest) => ({
      label: t("dashboard.other"),
      value: rest.reduce((sum, row) => sum + row.value, 0),
    })
  )

  const reasonRows = (rows: Schemas["ReasonCountDto"][] | undefined) =>
    (rows ?? []).map((row) => ({
      label: row.reason ?? t("common.unknown"),
      value: toNumber(row.count),
    }))

  const averageMinutes = session?.averageSessionMinutes

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          title={t("dashboard.lockedOutNow")}
          value={auth?.lockedOutNow ?? undefined}
          icon={Lock}
          loading={authStats.isLoading}
          hint={t("dashboard.lockoutEventsHint", {
            count: toNumber(auth?.lockoutEventsInWindow),
          })}
        />
        <StatTile
          title={t("dashboard.activeTokens")}
          value={session?.activeRefreshTokens ?? undefined}
          icon={KeySquare}
          loading={sessionStats.isLoading}
          hint={t("dashboard.tokensHint", {
            expiring: toNumber(session?.tokensExpiringIn7Days),
            revoked: toNumber(session?.tokensRevokedInWindow),
          })}
        />
        <StatTile
          title={t("dashboard.sessionsStarted")}
          value={session?.startedInWindow ?? undefined}
          icon={Timer}
          loading={sessionStats.isLoading}
          hint={t("dashboard.staleSessionsHint", {
            count: toNumber(session?.staleOpenSessions),
          })}
        />
        <StatTile
          title={t("dashboard.averageSession")}
          value={
            averageMinutes != null
              ? t("common.minutesShort", { count: Math.round(toNumber(averageMinutes)) })
              : "—"
          }
          icon={Timer}
          loading={sessionStats.isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <CountSeriesCard
          title={t("dashboard.failedAttempts")}
          description={t("dashboard.failedAttemptsSubtitle")}
          seriesLabel={t("dashboard.failedAttempts")}
          data={failureSeries}
          variant="bars"
          loading={authStats.isLoading}
          refetching={authStats.isFetching && !authStats.isLoading}
          exportName="failed-sign-in-attempts"
        />
        <RankedBarCard
          title={t("dashboard.failureReasons")}
          description={t("dashboard.failureReasonsSubtitle")}
          seriesLabel={t("dashboard.failedAttempts")}
          data={failureReasons}
          loading={authStats.isLoading}
          refetching={authStats.isFetching && !authStats.isLoading}
          exportName="sign-in-failure-reasons"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="lg:col-span-1">
          <IpTableCard
            data={auth?.topFailingIps ?? []}
            loading={authStats.isLoading}
          />
        </div>
        <RankedBarCard
          title={t("dashboard.sessionEndReasons")}
          description={t("dashboard.sessionEndReasonsSubtitle")}
          seriesLabel={t("dashboard.sessions")}
          data={reasonRows(session?.endReasons)}
          loading={sessionStats.isLoading}
          refetching={sessionStats.isFetching && !sessionStats.isLoading}
          exportName="session-end-reasons"
        />
        <RankedBarCard
          title={t("dashboard.revocationReasons")}
          description={t("dashboard.revocationReasonsSubtitle")}
          seriesLabel={t("dashboard.tokens")}
          data={reasonRows(session?.revocationReasons)}
          loading={sessionStats.isLoading}
          refetching={sessionStats.isFetching && !sessionStats.isLoading}
          exportName="token-revocation-reasons"
        />
      </div>
    </div>
  )
}
