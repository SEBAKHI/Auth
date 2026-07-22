import { useQuery } from "@tanstack/react-query"
import {
  Activity,
  AppWindow,
  Building2,
  KeyRound,
  KeySquare,
  Lock,
  MonitorSmartphone,
  ScrollText,
  ShieldCheck,
  UserMinus,
  UserPlus,
  Users as UsersIcon,
} from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { Bar, BarChart, XAxis, YAxis } from "recharts"
import { Area, AreaChart, CartesianGrid } from "recharts"
import { format, parseISO } from "date-fns"

import { PageHeader } from "@astoom/ui/common/page-header"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@astoom/ui/chart"
import type { ChartConfig } from "@astoom/ui/chart"
import { Skeleton } from "@astoom/ui/skeleton"
import { api } from "@astoom/api/client"
import { toNumber, unwrap } from "@astoom/api/helpers"
import type { Schemas } from "@astoom/api/types"
import { useAuth } from "@astoom/auth/auth-context"
import {
  getTimeZoneOffsetLabel,
  useActiveTimeZone,
} from "@astoom/i18n/timezone"
import { PERMISSIONS } from "@/lib/constants"
import { formatRelative, userStatusMeta } from "@astoom/ui/format"

import { AppActivityTableCard } from "./app-activity-table-card"
import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"
import { DailyCountBarCard } from "./daily-count-bar-card"
import { DailyLoginsCard } from "./daily-logins-card"
import { DonutCard } from "./donut-card"
import type { Slice } from "./donut-card"
import { EnablementMatrixCard } from "./enablement-matrix-card"
import { FunnelCard } from "./funnel-card"
import {
  bucketDaily,
  buildCountSeries,
  buildLoginSeries,
  pctDelta,
  successRate,
  topNWithOther,
} from "./helpers"
import { IpTableCard } from "./ip-table-card"
import { OutcomeBarsCard } from "./outcome-bars-card"
import { RankedBarCard } from "./ranked-bar-card"
import { StatCard } from "./stat-card"
import { TrendAreaCard } from "./trend-area-card"

/** Trailing analysis window; every aggregate endpoint is queried with it. */
const WINDOW_DAYS = 30
/** Window of the audit-event cards (kept from the previous dashboard). */
const AUDIT_DAYS = 14
const DAY_MS = 86_400_000

type AuditLog = Schemas["AuditLogDto"]

/** Top actions by frequency (real `action`, e.g. `user.login`). */
function buildActions(logs: AuditLog[], top = 6) {
  const counts = new Map<string, number>()
  for (const log of logs) {
    const key = log.action || "—"
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, top)
    .map(([action, count]) => ({ action, count }))
}

/** Audit events grouped by affected entity type, capped to the palette. */
function buildEntityBreakdown(
  logs: AuditLog[],
  unknownLabel: string,
  otherLabel: string
): Slice[] {
  const counts = new Map<string, number>()
  for (const log of logs) {
    const key = log.entityType || "other"
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  const slices = [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([key, value]) => ({
      key,
      label: key === "other" ? unknownLabel : key,
      value,
    }))
  return topNWithOther(slices, 4, (rest) => ({
    key: "folded",
    label: otherLabel,
    value: rest.reduce((t, s) => t + s.value, 0),
  }))
}

export function DashboardPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const timeZone = useActiveTimeZone()
  const timeZoneOffset = getTimeZoneOffsetLabel(timeZone)
  const timeZoneLabel = `\u2066${timeZoneOffset ? `${timeZone} (${timeZoneOffset})` : timeZone}\u2069`

  const canUsers = hasPermission(PERMISSIONS.users.read)
  const canApps = hasPermission(PERMISSIONS.applications.read)
  const canRoles = hasPermission(PERMISSIONS.roles.read)
  const canAudit = hasPermission(PERMISSIONS.auditLogs.read)
  const canReadAllOrganizations = hasPermission(PERMISSIONS.organizations.read)

  // ─── Server-side aggregates (full-table SQL, never a page) ────────────────
  const userStatsQuery = useQuery({
    queryKey: ["dashboard", "user-stats", WINDOW_DAYS, timeZone],
    enabled: canUsers,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/user-stats", {
          params: { query: { days: WINDOW_DAYS, timeZone } },
        })
      ),
  })

  const authStatsQuery = useQuery({
    queryKey: ["dashboard", "auth-stats", WINDOW_DAYS, timeZone],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/auth-stats", {
          params: { query: { days: WINDOW_DAYS, timeZone } },
        })
      ),
  })

  const sessionStatsQuery = useQuery({
    queryKey: ["dashboard", "session-stats", WINDOW_DAYS],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/session-stats", {
          params: { query: { days: WINDOW_DAYS } },
        })
      ),
  })

  const appActivityQuery = useQuery({
    queryKey: ["dashboard", "app-activity", WINDOW_DAYS],
    enabled: canApps,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/dashboard/app-activity", {
          params: { query: { days: WINDOW_DAYS } },
        })
      ),
  })

  const memberOrgsQuery = useQuery({
    queryKey: ["dashboard", "orgs", "membership"],
    enabled: !canReadAllOrganizations,
    queryFn: () => unwrap(api.GET("/api/v1/Organizations")),
  })

  const allOrgsQuery = useQuery({
    queryKey: ["dashboard", "orgs", "all"],
    enabled: canReadAllOrganizations,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/all", {
          params: { query: { pageNumber: 1, pageSize: 1 } },
        })
      ),
  })

  // ─── Audit-event cards (accurate totals; series page-limited and flagged) ──
  const eventsWeekQuery = useQuery({
    queryKey: ["dashboard", "audit-week"],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: 1,
              pageSize: 1,
              fromDate: new Date(Date.now() - 7 * DAY_MS).toISOString(),
            },
          },
        })
      ),
  })

  const prevWeekQuery = useQuery({
    queryKey: ["dashboard", "audit-prev-week"],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: 1,
              pageSize: 1,
              fromDate: new Date(Date.now() - 14 * DAY_MS).toISOString(),
              toDate: new Date(Date.now() - 7 * DAY_MS).toISOString(),
            },
          },
        })
      ),
  })

  const seriesQuery = useQuery({
    queryKey: ["dashboard", "audit-series"],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: {
              pageNumber: 1,
              pageSize: 100,
              fromDate: new Date(
                Date.now() - AUDIT_DAYS * DAY_MS
              ).toISOString(),
            },
          },
        })
      ),
  })

  const recentQuery = useQuery({
    queryKey: ["dashboard", "recent"],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: { query: { pageNumber: 1, pageSize: 8 } },
        })
      ),
  })

  const userStats = userStatsQuery.data
  const authStats = authStatsQuery.data
  const sessionStats = sessionStatsQuery.data
  const appActivity = appActivityQuery.data

  // ─── Derived series and breakdowns ─────────────────────────────────────────
  const loginSeries = buildLoginSeries(
    authStats?.loginsPerDay ?? [],
    WINDOW_DAYS,
    timeZone
  )
  const dauSeries = buildCountSeries(
    authStats?.activeUsersPerDay ?? [],
    WINDOW_DAYS,
    timeZone
  )
  const signupSeries = buildCountSeries(
    userStats?.signupsPerDay ?? [],
    WINDOW_DAYS,
    timeZone
  )

  const windowSuccess = toNumber(authStats?.windowSuccessCount)
  const windowFailure = toNumber(authStats?.windowFailureCount)
  const prevSuccess = toNumber(authStats?.previousWindowSuccessCount)
  const prevFailure = toNumber(authStats?.previousWindowFailureCount)
  const rate = successRate(windowSuccess, windowFailure)
  const prevRate = successRate(prevSuccess, prevFailure)
  const rateDelta =
    rate !== null && prevRate !== null
      ? Math.round((rate - prevRate) * 10) / 10
      : null

  const activeUsers = toNumber(userStats?.activeUsers)
  const mfaEnabled = toNumber(userStats?.mfaEnabled)
  const mfaAdoption =
    activeUsers > 0 ? `${Math.round((mfaEnabled / activeUsers) * 100)}%` : "—"

  const userStatusData: Slice[] = (userStats?.byStatus ?? []).map((row) => {
    const meta = userStatusMeta(row.status)
    return {
      key: meta.key,
      label: t(`common.${meta.key}`),
      value: toNumber(row.count),
    }
  })

  const failureReasonRows = topNWithOther(
    (authStats?.failureReasons ?? []).map((r) => ({
      label: r.reason ?? t("common.unknown"),
      value: toNumber(r.count),
    })),
    8,
    (rest) => ({
      label: t("dashboard.other"),
      value: rest.reduce((total, r) => total + r.value, 0),
    })
  )

  const loginsByAppRows = (authStats?.loginsByApplication ?? []).map((row) => ({
    label: row.applicationName ?? t("common.unknown"),
    success: toNumber(row.successCount),
    failure: toNumber(row.failureCount),
  }))

  const loginsByOrgRows = (authStats?.loginsByOrganization ?? []).map(
    (row) => ({
      label: row.organizationName ?? t("dashboard.unattributed"),
      success: toNumber(row.successCount),
      failure: toNumber(row.failureCount),
    })
  )

  const orgMembers = (userStats?.usersByOrganization ?? []).map((row) => ({
    label: row.organizationName ?? t("common.unknown"),
    value: toNumber(row.count),
  }))
  const listedMembers = orgMembers.reduce((total, r) => total + r.value, 0)
  const memberRemainder =
    toNumber(userStats?.totalActiveMemberships) - listedMembers
  const usersByOrgRows =
    memberRemainder > 0
      ? [...orgMembers, { label: t("dashboard.other"), value: memberRemainder }]
      : orgMembers

  const capReasons = (rows: Schemas["ReasonCountDto"][] | undefined): Slice[] =>
    topNWithOther(
      (rows ?? []).map((r, i) => ({
        key: `r${i}`,
        label: r.reason ?? t("common.unknown"),
        value: toNumber(r.count),
      })),
      4,
      (rest) => ({
        key: "folded",
        label: t("dashboard.other"),
        value: rest.reduce((total, s) => total + s.value, 0),
      })
    )
  const endReasonSlices = capReasons(sessionStats?.endReasons)
  const revocationSlices = capReasons(sessionStats?.revocationReasons)

  // ─── Audit-event derivations (existing cards) ──────────────────────────────
  const seriesLogs = seriesQuery.data?.logs ?? []
  const seriesTotal = toNumber(seriesQuery.data?.totalCount)
  const seriesTruncated = seriesTotal > seriesLogs.length
  const timeseries = bucketDaily(
    seriesLogs.map((log) => log.timestamp),
    AUDIT_DAYS,
    timeZone
  )
  const actions = buildActions(seriesLogs)
  const entityBreakdown = buildEntityBreakdown(
    seriesLogs,
    t("common.unknown"),
    t("dashboard.other")
  )
  const recent = recentQuery.data?.logs ?? []

  const eventsThisWeek = toNumber(eventsWeekQuery.data?.totalCount)
  const eventsPrevWeek = toNumber(prevWeekQuery.data?.totalCount)
  const eventsDelta = pctDelta(eventsThisWeek, eventsPrevWeek)

  const seriesConfig = {
    events: { label: t("dashboard.events"), color: SERIES.area },
  } satisfies ChartConfig

  const actionsConfig = {
    count: { label: t("dashboard.events"), color: SERIES.primary },
  } satisfies ChartConfig

  const auditSubtitle = seriesTruncated
    ? `${t("dashboard.eventsOverTimeSubtitle")} ${t("dashboard.sampleNote", {
        shown: seriesLogs.length,
        total: seriesTotal,
      })}`
    : t("dashboard.eventsOverTimeSubtitle")

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("dashboard.title")}
        description={t("dashboard.subtitleWindow", {
          days: WINDOW_DAYS,
          timeZone: timeZoneLabel,
        })}
      />

      {/* ─── Headline numbers ─────────────────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-5">
        {canUsers ? (
          <StatCard
            title={t("dashboard.totalUsers")}
            value={userStats?.totalUsers ?? undefined}
            icon={UsersIcon}
            loading={userStatsQuery.isLoading}
            hint={t("dashboard.newUsersHint", {
              count: toNumber(userStats?.newInWindow),
            })}
          />
        ) : null}
        {canAudit ? (
          <StatCard
            title={t("dashboard.activeUsersWindow")}
            value={authStats?.activeUsersInWindow ?? undefined}
            icon={Activity}
            loading={authStatsQuery.isLoading}
          />
        ) : null}
        {canAudit ? (
          <StatCard
            title={t("dashboard.successRate")}
            value={rate !== null ? `${rate}%` : "—"}
            icon={ShieldCheck}
            loading={authStatsQuery.isLoading}
            delta={rateDelta}
          />
        ) : null}
        {canAudit ? (
          <StatCard
            title={t("dashboard.activeSessions")}
            value={sessionStats?.activeSessions ?? undefined}
            icon={MonitorSmartphone}
            loading={sessionStatsQuery.isLoading}
            hint={t("dashboard.staleSessionsHint", {
              count: toNumber(sessionStats?.staleOpenSessions),
            })}
          />
        ) : null}
        {canAudit ? (
          <StatCard
            title={t("dashboard.lockedOutNow")}
            value={authStats?.lockedOutNow ?? undefined}
            icon={Lock}
            loading={authStatsQuery.isLoading}
            hint={t("dashboard.lockoutEventsHint", {
              count: toNumber(authStats?.lockoutEventsInWindow),
            })}
          />
        ) : null}
        {canUsers ? (
          <StatCard
            title={t("dashboard.mfaAdoption")}
            value={mfaAdoption}
            icon={ShieldCheck}
            loading={userStatsQuery.isLoading}
            hint={t("dashboard.mfaAdoptionHint", {
              enabled: mfaEnabled,
              active: activeUsers,
            })}
          />
        ) : null}
        {canUsers ? (
          <StatCard
            title={t("dashboard.dormantAccounts")}
            value={userStats?.dormantOver30Days ?? undefined}
            icon={UserMinus}
            loading={userStatsQuery.isLoading}
            hint={t("dashboard.dormantHint", {
              d60: toNumber(userStats?.dormantOver60Days),
              d90: toNumber(userStats?.dormantOver90Days),
            })}
          />
        ) : null}
        {canAudit ? (
          <StatCard
            title={t("dashboard.activeTokens")}
            value={sessionStats?.activeRefreshTokens ?? undefined}
            icon={KeySquare}
            loading={sessionStatsQuery.isLoading}
            hint={t("dashboard.tokensHint", {
              expiring: toNumber(sessionStats?.tokensExpiringIn7Days),
              revoked: toNumber(sessionStats?.tokensRevokedInWindow),
            })}
          />
        ) : null}
        <StatCard
          title={t(
            canReadAllOrganizations
              ? "dashboard.totalOrganizations"
              : "dashboard.myOrganizations"
          )}
          value={
            canReadAllOrganizations
              ? toNumber(allOrgsQuery.data?.totalCount)
              : memberOrgsQuery.data?.length
          }
          icon={Building2}
          loading={
            canReadAllOrganizations
              ? allOrgsQuery.isLoading
              : memberOrgsQuery.isLoading
          }
        />
        {canAudit ? (
          <StatCard
            title={t("dashboard.eventsTrend")}
            value={eventsThisWeek}
            icon={ScrollText}
            loading={eventsWeekQuery.isLoading}
            delta={eventsDelta}
          />
        ) : null}
      </div>

      {/* ─── Login activity ───────────────────────────────────────────────── */}
      {canAudit ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <div className="lg:col-span-2">
            <DailyLoginsCard
              data={loginSeries}
              loading={authStatsQuery.isLoading}
              description={t("dashboard.loginOutcomesSubtitle", {
                timeZone: timeZoneLabel,
              })}
            />
          </div>
          <RankedBarCard
            title={t("dashboard.failureReasons")}
            description={t("dashboard.failureReasonsSubtitle")}
            seriesLabel={t("dashboard.failedAttempts")}
            data={failureReasonRows}
            loading={authStatsQuery.isLoading}
          />
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-3">
        {canAudit ? (
          <div className="lg:col-span-2">
            <TrendAreaCard
              title={t("dashboard.dailyActiveUsers")}
              description={t("dashboard.dailyActiveUsersSubtitle", {
                timeZone: timeZoneLabel,
              })}
              seriesLabel={t("dashboard.activeUsersWindow")}
              data={dauSeries}
              loading={authStatsQuery.isLoading}
            />
          </div>
        ) : null}
        {canUsers ? (
          <DonutCard
            title={t("dashboard.usersByStatus")}
            description={t("dashboard.usersByStatusSubtitle")}
            data={userStatusData}
            loading={userStatsQuery.isLoading}
          />
        ) : null}
      </div>

      {/* ─── Splits by application and organization ───────────────────────── */}
      {canAudit ? (
        <div className="grid gap-4 lg:grid-cols-2">
          <OutcomeBarsCard
            title={t("dashboard.loginsByApplication")}
            description={t("dashboard.loginsByApplicationSubtitle")}
            data={loginsByAppRows}
            loading={authStatsQuery.isLoading}
          />
          <OutcomeBarsCard
            title={t("dashboard.loginsByOrganization")}
            description={t("dashboard.loginsByOrganizationSubtitle")}
            data={loginsByOrgRows}
            loading={authStatsQuery.isLoading}
          />
        </div>
      ) : null}

      {/* ─── Growth and lifecycle ─────────────────────────────────────────── */}
      {canUsers ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <FunnelCard
            created={toNumber(userStats?.cohortCreated)}
            confirmed={toNumber(userStats?.cohortEmailConfirmed)}
            loggedIn={toNumber(userStats?.cohortLoggedIn)}
            loading={userStatsQuery.isLoading}
          />
          <DailyCountBarCard
            title={t("dashboard.signups")}
            description={t("dashboard.signupsSubtitle", {
              timeZone: timeZoneLabel,
            })}
            seriesLabel={t("dashboard.signupsSeries")}
            data={signupSeries}
            loading={userStatsQuery.isLoading}
          />
          <RankedBarCard
            title={t("dashboard.usersByOrganization")}
            description={t("dashboard.usersByOrganizationSubtitle")}
            seriesLabel={t("dashboard.members")}
            data={usersByOrgRows}
            loading={userStatsQuery.isLoading}
          />
        </div>
      ) : null}

      {/* ─── Applications and organizations ───────────────────────────────── */}
      {canApps ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <AppActivityTableCard
            data={appActivity?.applications ?? []}
            loading={appActivityQuery.isLoading}
          />
          <div className="lg:col-span-2">
            <EnablementMatrixCard
              data={appActivity?.organizationApplications ?? []}
              loading={appActivityQuery.isLoading}
            />
          </div>
        </div>
      ) : null}

      {/* ─── Security and hygiene detail ──────────────────────────────────── */}
      {canAudit ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <IpTableCard
            data={authStats?.topFailingIps ?? []}
            loading={authStatsQuery.isLoading}
          />
          <DonutCard
            title={t("dashboard.sessionEndReasons")}
            description={t("dashboard.sessionEndReasonsSubtitle")}
            data={endReasonSlices}
            loading={sessionStatsQuery.isLoading}
          />
          <DonutCard
            title={t("dashboard.revocationReasons")}
            description={t("dashboard.revocationReasonsSubtitle")}
            data={revocationSlices}
            loading={sessionStatsQuery.isLoading}
          />
        </div>
      ) : null}

      {/* ─── Audit events (accurate headline; series flagged when sampled) ─── */}
      {canAudit ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <Card className="lg:col-span-2">
            <CardHeader>
              <CardTitle>{t("dashboard.eventsOverTime")}</CardTitle>
              <CardDescription>{auditSubtitle}</CardDescription>
            </CardHeader>
            <CardContent>
              {seriesQuery.isLoading ? (
                <Skeleton className="h-[240px] w-full" />
              ) : (
                <ChartContainer
                  config={seriesConfig}
                  dir="ltr"
                  className="aspect-auto h-[240px] w-full"
                >
                  <AreaChart
                    data={timeseries.map((p) => ({
                      label: format(parseISO(p.day), "MMM d"),
                      events: p.count,
                    }))}
                    margin={{ left: 12, right: 12, top: 8 }}
                  >
                    <CartesianGrid vertical={false} />
                    {/* Hidden axis adds top headroom so peaks don't clip
                        against the plot edge. */}
                    <YAxis
                      hide
                      domain={[
                        0,
                        (dataMax: number) =>
                          Math.max(1, Math.ceil(dataMax * 1.25)),
                      ]}
                    />
                    <XAxis
                      dataKey="label"
                      tickLine={false}
                      axisLine={false}
                      tickMargin={8}
                      minTickGap={24}
                    />
                    <ChartTooltip
                      cursor={false}
                      content={<ChartTooltipContent indicator="dot" />}
                    />
                    <Area
                      dataKey="events"
                      type="monotone"
                      stroke="var(--color-events)"
                      fill="var(--color-events)"
                      fillOpacity={0.25}
                    />
                  </AreaChart>
                </ChartContainer>
              )}
            </CardContent>
          </Card>

          <DonutCard
            title={t("dashboard.eventsByEntity")}
            description={t("dashboard.eventsByEntitySubtitle")}
            data={entityBreakdown}
            loading={seriesQuery.isLoading}
          />
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-3">
        {canAudit ? (
          <Card className="lg:col-span-2">
            <CardHeader>
              <CardTitle>{t("dashboard.topActions")}</CardTitle>
              <CardDescription>
                {t("dashboard.topActionsSubtitle")}
              </CardDescription>
            </CardHeader>
            <CardContent>
              {seriesQuery.isLoading ? (
                <Skeleton className="h-[240px] w-full" />
              ) : actions.length > 0 ? (
                <ChartContainer
                  config={actionsConfig}
                  dir="ltr"
                  className="aspect-auto h-[240px] w-full"
                >
                  <BarChart
                    data={actions}
                    layout="vertical"
                    margin={{ left: 12, right: 16 }}
                  >
                    <XAxis type="number" dataKey="count" hide />
                    <YAxis
                      type="category"
                      dataKey="action"
                      tickLine={false}
                      axisLine={false}
                      width={140}
                      tickFormatter={(value: string) =>
                        value.length > 18 ? `${value.slice(0, 18)}…` : value
                      }
                    />
                    <ChartTooltip
                      cursor={false}
                      content={<ChartTooltipContent hideLabel />}
                    />
                    <Bar dataKey="count" fill="var(--color-count)" radius={5} />
                  </BarChart>
                </ChartContainer>
              ) : (
                <ChartEmpty />
              )}
            </CardContent>
          </Card>
        ) : null}

        <Card>
          <CardHeader>
            <CardTitle>{t("dashboard.quickActions")}</CardTitle>
            <CardDescription>
              {t("dashboard.quickActionsSubtitle")}
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-2">
            {canUsers ? (
              <Button asChild variant="outline" className="justify-start">
                <Link to="/users">
                  <UserPlus />
                  {t("dashboard.inviteUser")}
                </Link>
              </Button>
            ) : null}
            {canApps ? (
              <Button asChild variant="outline" className="justify-start">
                <Link to="/applications">
                  <AppWindow />
                  {t("dashboard.newApplication")}
                </Link>
              </Button>
            ) : null}
            {canRoles ? (
              <Button asChild variant="outline" className="justify-start">
                <Link to="/roles">
                  <KeyRound />
                  {t("dashboard.manageRoles")}
                </Link>
              </Button>
            ) : null}
            <Button asChild variant="outline" className="justify-start">
              <Link to="/audit-logs">
                <ScrollText />
                {t("dashboard.viewAuditLogs")}
              </Link>
            </Button>
          </CardContent>
        </Card>
      </div>

      {canAudit ? (
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle>{t("dashboard.recentActivity")}</CardTitle>
            <Link
              to="/audit-logs"
              className="text-sm text-muted-foreground underline-offset-4 hover:underline"
            >
              {t("dashboard.viewAll")}
            </Link>
          </CardHeader>
          <CardContent>
            {recentQuery.isLoading ? (
              <div className="space-y-2">
                {Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-10 w-full" />
                ))}
              </div>
            ) : recent.length === 0 ? (
              <p className="py-6 text-center text-sm text-muted-foreground">
                {t("dashboard.noActivity")}
              </p>
            ) : (
              <ul className="divide-y">
                {recent.map((log) => (
                  <li
                    key={log.id}
                    className="flex items-center justify-between gap-3 py-2.5 text-sm"
                  >
                    <div className="min-w-0">
                      <p className="truncate font-medium">{log.action}</p>
                      <p className="truncate text-xs text-muted-foreground">
                        {log.userEmail ?? log.userName ?? "—"}
                      </p>
                    </div>
                    <div className="flex shrink-0 items-center gap-3">
                      {log.entityType ? (
                        <Badge variant="outline">{log.entityType}</Badge>
                      ) : null}
                      <span className="text-xs text-muted-foreground">
                        {formatRelative(log.timestamp)}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      ) : null}
    </div>
  )
}
