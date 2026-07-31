import { useTranslation } from "react-i18next"
import { Bar, BarChart, XAxis, YAxis } from "recharts"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@authsystem/ui/chart"
import type { ChartConfig } from "@authsystem/ui/chart"
import { Skeleton } from "@authsystem/ui/skeleton"

import { SERIES } from "./chart-constants"
import { Empty, EmptyHeader, EmptyTitle } from "@authsystem/ui/empty"

/**
 * Three-stage activation funnel for the window cohort:
 * created → email confirmed → signed in. Stage order is fixed, never sorted.
 */
export function FunnelCard({
  created,
  confirmed,
  loggedIn,
  loading,
}: {
  created: number
  confirmed: number
  loggedIn: number
  loading: boolean
}) {
  const { t } = useTranslation()

  const rows = [
    { stage: t("dashboard.funnelCreated"), value: created },
    { stage: t("dashboard.funnelConfirmed"), value: confirmed },
    { stage: t("dashboard.funnelLoggedIn"), value: loggedIn },
  ]
  const pct = (part: number) =>
    created > 0 ? `${Math.round((part / created) * 100)}%` : "—"

  const config = {
    value: { label: t("dashboard.funnelUsers"), color: SERIES.primary },
  } satisfies ChartConfig

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.funnel")}</CardTitle>
        <CardDescription>{t("dashboard.funnelSubtitle")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {loading ? (
          <Skeleton className="h-[200px] w-full" />
        ) : created > 0 ? (
          <>
            <ChartContainer
              config={config}
              dir="ltr"
              className="aspect-auto h-[180px] w-full"
            >
              <BarChart
                data={rows}
                layout="vertical"
                margin={{ left: 12, right: 16 }}
              >
                <XAxis type="number" hide />
                <YAxis
                  type="category"
                  dataKey="stage"
                  tickLine={false}
                  axisLine={false}
                  width={140}
                />
                <ChartTooltip
                  cursor={false}
                  content={<ChartTooltipContent hideLabel />}
                />
                <Bar dataKey="value" fill="var(--color-value)" radius={5} />
              </BarChart>
            </ChartContainer>
            <p className="text-xs text-muted-foreground">
              {t("dashboard.funnelRates", {
                confirmed: pct(confirmed),
                loggedIn: pct(loggedIn),
              })}
            </p>
          </>
        ) : (
          <Empty className="py-8">
            <EmptyHeader>
              <EmptyTitle>{t("dashboard.noData")}</EmptyTitle>
            </EmptyHeader>
          </Empty>
        )}
      </CardContent>
    </Card>
  )
}
