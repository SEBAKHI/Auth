import { useQuery } from "@tanstack/react-query"
import { format, parseISO } from "date-fns"
import {
  Activity,
  AppWindow,
  Building2,
  KeyRound,
  ScrollText,
  TrendingDown,
  TrendingUp,
  UserPlus,
  Users as UsersIcon,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  XAxis,
  YAxis,
} from "recharts"

import { PageHeader } from "@/components/common/page-header"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from "@/components/ui/chart"
import type { ChartConfig } from "@/components/ui/chart"
import { Skeleton } from "@/components/ui/skeleton"
import { api } from "@/lib/api/client"
import { toNumber, unwrap } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { formatRelative, userStatusMeta } from "@/lib/format"
import type { Schemas } from "@/lib/api/types"

const DAY_MS = 86_400_000
const SEVEN_DAYS_AGO = new Date(Date.now() - 7 * DAY_MS).toISOString()
const FOURTEEN_DAYS_AGO = new Date(Date.now() - 14 * DAY_MS).toISOString()

/** Categorical color palette mapped to the preset chart tokens. */
const PALETTE = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
]

type AuditLog = Schemas["AuditLogDto"]
type User = Schemas["UserDto"]
type Application = Schemas["ApplicationDto"]

/** A single slice of a donut/category breakdown. */
type Slice = { key: string; label: string; value: number }

function StatCard({
  title,
  value,
  icon: Icon,
  loading,
  delta,
}: {
  title: string
  value: number | string | undefined
  icon: LucideIcon
  loading: boolean
  delta?: number | null
}) {
  const { t } = useTranslation()
  return (
    <Card>
      <CardHeader className="flex flex-row items-center gap-3 pb-2">
        <span className="flex size-9 items-center justify-center rounded-2xl bg-muted text-muted-foreground">
          <Icon className="size-5" />
        </span>
        <CardTitle className="text-sm font-medium text-muted-foreground">
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1">
        {loading ? (
          <Skeleton className="h-8 w-16" />
        ) : (
          <p className="text-3xl font-semibold tabular-nums">{value ?? "—"}</p>
        )}
        {delta != null && !loading ? (
          <p className="flex items-center gap-1 text-xs text-muted-foreground">
            {delta >= 0 ? (
              <TrendingUp className="size-3.5" />
            ) : (
              <TrendingDown className="size-3.5" />
            )}
            <span className="tabular-nums">
              {delta >= 0 ? "+" : ""}
              {delta}%
            </span>
            {t("dashboard.vsPrevious")}
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}

/** Donut chart card driven by a category breakdown. */
function DonutCard({
  title,
  description,
  data,
  loading,
}: {
  title: string
  description: string
  data: Slice[]
  loading: boolean
}) {
  const { t } = useTranslation()
  const config: ChartConfig = {}
  data.forEach((slice, i) => {
    config[slice.key] = { label: slice.label, color: PALETTE[i % PALETTE.length] }
  })
  const chartData = data.map((slice) => ({
    name: slice.key,
    value: slice.value,
    fill: `var(--color-${slice.key})`,
  }))
  const hasData = data.some((slice) => slice.value > 0)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent>
        {loading ? (
          <Skeleton className="mx-auto aspect-square max-h-[220px]" />
        ) : hasData ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className="mx-auto aspect-square max-h-[220px]"
          >
            <PieChart>
              <ChartTooltip
                cursor={false}
                content={<ChartTooltipContent nameKey="name" hideLabel />}
              />
              <Pie
                data={chartData}
                dataKey="value"
                nameKey="name"
                innerRadius={55}
                strokeWidth={4}
              >
                {chartData.map((entry) => (
                  <Cell key={entry.name} fill={entry.fill} />
                ))}
              </Pie>
              <ChartLegend content={<ChartLegendContent nameKey="name" />} />
            </PieChart>
          </ChartContainer>
        ) : (
          <p className="py-12 text-center text-sm text-muted-foreground">
            {t("dashboard.noData")}
          </p>
        )}
      </CardContent>
    </Card>
  )
}

/** Buckets audit logs into per-day event counts over `days` (local dates). */
function buildTimeseries(logs: AuditLog[], days = 14) {
  const buckets = new Map<string, { label: string; events: number }>()
  const start = new Date()
  start.setHours(0, 0, 0, 0)
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(start.getTime() - i * DAY_MS)
    buckets.set(format(d, "yyyy-MM-dd"), {
      label: format(d, "MMM d"),
      events: 0,
    })
  }
  for (const log of logs) {
    if (!log.timestamp) continue
    const bucket = buckets.get(format(parseISO(log.timestamp), "yyyy-MM-dd"))
    if (bucket) bucket.events++
  }
  return [...buckets.values()]
}

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

/** Audit events grouped by affected entity type. */
function buildEntityBreakdown(logs: AuditLog[], unknownLabel: string): Slice[] {
  const counts = new Map<string, number>()
  for (const log of logs) {
    const key = log.entityType || "other"
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([key, value]) => ({
      key,
      label: key === "other" ? unknownLabel : key,
      value,
    }))
}

/** Users grouped by account status. */
function buildUserStatusBreakdown(
  users: User[],
  label: (key: string) => string
): Slice[] {
  const counts = new Map<string, number>()
  for (const user of users) {
    const { key } = userStatusMeta(user.status)
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return [...counts.entries()].map(([key, value]) => ({
    key,
    label: label(key),
    value,
  }))
}

export function DashboardPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const canUsers = hasPermission(PERMISSIONS.users.read)
  const canApps = hasPermission(PERMISSIONS.applications.read)
  const canRoles = hasPermission(PERMISSIONS.roles.read)
  const canAudit = hasPermission(PERMISSIONS.auditLogs.read)

  const usersQuery = useQuery({
    queryKey: ["dashboard", "users"],
    enabled: canUsers,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users", {
          params: { query: { pageNumber: 1, pageSize: 100 } },
        })
      ),
  })

  const appsQuery = useQuery({
    queryKey: ["dashboard", "apps"],
    enabled: canApps,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications", {
          params: { query: { pageNumber: 1, pageSize: 100 } },
        })
      ),
  })

  const orgsQuery = useQuery({
    queryKey: ["dashboard", "orgs"],
    queryFn: () => unwrap(api.GET("/api/v1/Organizations")),
  })

  const eventsWeekQuery = useQuery({
    queryKey: ["dashboard", "audit-week"],
    enabled: canAudit,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/audit-logs", {
          params: {
            query: { pageNumber: 1, pageSize: 1, fromDate: SEVEN_DAYS_AGO },
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
              fromDate: FOURTEEN_DAYS_AGO,
              toDate: SEVEN_DAYS_AGO,
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
              fromDate: FOURTEEN_DAYS_AGO,
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

  const seriesLogs = seriesQuery.data?.logs ?? []
  const timeseries = buildTimeseries(seriesLogs)
  const actions = buildActions(seriesLogs)
  const entityBreakdown = buildEntityBreakdown(seriesLogs, t("common.unknown"))
  const recent = recentQuery.data?.logs ?? []

  const userStatusData = buildUserStatusBreakdown(
    usersQuery.data?.users ?? [],
    (key) => t(`common.${key}`)
  )

  const apps = appsQuery.data?.applications ?? []
  const activeApps = apps.filter((app: Application) => app.isActive).length
  const appStatusData: Slice[] = [
    { key: "active", label: t("common.active"), value: activeApps },
    {
      key: "inactive",
      label: t("common.inactive"),
      value: apps.length - activeApps,
    },
  ]

  const eventsThisWeek = toNumber(eventsWeekQuery.data?.totalCount)
  const eventsPrevWeek = toNumber(prevWeekQuery.data?.totalCount)
  const eventsDelta =
    eventsPrevWeek > 0
      ? Math.round(((eventsThisWeek - eventsPrevWeek) / eventsPrevWeek) * 100)
      : null

  const seriesConfig = {
    events: { label: t("dashboard.events"), color: "var(--chart-1)" },
  } satisfies ChartConfig

  const actionsConfig = {
    count: { label: t("dashboard.events"), color: "var(--chart-2)" },
  } satisfies ChartConfig

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("dashboard.title")}
        description={t("dashboard.subtitle")}
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {canUsers ? (
          <StatCard
            title={t("dashboard.totalUsers")}
            value={usersQuery.data?.totalCount}
            icon={UsersIcon}
            loading={usersQuery.isLoading}
          />
        ) : null}
        {canApps ? (
          <StatCard
            title={t("dashboard.totalApplications")}
            value={appsQuery.data?.totalCount}
            icon={AppWindow}
            loading={appsQuery.isLoading}
          />
        ) : null}
        <StatCard
          title={t("dashboard.totalOrganizations")}
          value={orgsQuery.data?.length}
          icon={Building2}
          loading={orgsQuery.isLoading}
        />
        {canAudit ? (
          <StatCard
            title={t("dashboard.eventsTrend")}
            value={eventsThisWeek}
            icon={Activity}
            loading={eventsWeekQuery.isLoading}
            delta={eventsDelta}
          />
        ) : null}
      </div>

      {canAudit ? (
        <div className="grid gap-4 lg:grid-cols-3">
          <Card className="lg:col-span-2">
            <CardHeader>
              <CardTitle>{t("dashboard.eventsOverTime")}</CardTitle>
              <CardDescription>
                {t("dashboard.eventsOverTimeSubtitle")}
              </CardDescription>
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
                    data={timeseries}
                    margin={{ left: 12, right: 12, top: 8 }}
                  >
                    <CartesianGrid vertical={false} />
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
                      type="natural"
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
                <p className="py-12 text-center text-sm text-muted-foreground">
                  {t("dashboard.noData")}
                </p>
              )}
            </CardContent>
          </Card>
        ) : null}

        {canUsers ? (
          <DonutCard
            title={t("dashboard.usersByStatus")}
            description={t("dashboard.usersByStatusSubtitle")}
            data={userStatusData}
            loading={usersQuery.isLoading}
          />
        ) : null}
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        {canApps ? (
          <DonutCard
            title={t("dashboard.applicationsStatus")}
            description={t("dashboard.applicationsStatusSubtitle")}
            data={appStatusData}
            loading={appsQuery.isLoading}
          />
        ) : null}

        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>{t("dashboard.quickActions")}</CardTitle>
            <CardDescription>
              {t("dashboard.quickActionsSubtitle")}
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-2 sm:grid-cols-2">
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
