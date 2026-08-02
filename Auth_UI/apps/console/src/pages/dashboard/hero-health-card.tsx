import { TrendingDown, TrendingUp } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from "@authsystem/ui/chart"
import type { ChartConfig } from "@authsystem/ui/chart"
import { Skeleton } from "@authsystem/ui/skeleton"
import { numberLocale } from "@authsystem/ui/format"

import { SERIES } from "./chart-constants"
import { formatBucket } from "./format-bucket"
import type { LoginPoint } from "./helpers"

/**
 * The dashboard's lead: can people sign in, and is that getting better or worse.
 *
 * A hero figure rather than a chart, because the reader's first question is a
 * single number. The attempt series sits beside it as context, stacked by outcome
 * — a status job, so failures take the semantic destructive token rather than
 * another step of the sequential ramp.
 */
export function HeroHealthCard({
  rate,
  rateDelta,
  success,
  failure,
  series,
  loading,
  description,
}: {
  rate: number | null
  rateDelta: number | null
  success: number
  failure: number
  series: LoginPoint[]
  loading: boolean
  description: string
}) {
  const { t } = useTranslation()

  const config = {
    success: { label: t("dashboard.success"), color: SERIES.primary },
    failure: { label: t("dashboard.failed"), color: SERIES.failure },
  } satisfies ChartConfig

  const hasData = series.some((p) => p.success > 0 || p.failure > 0)
  const locale = numberLocale()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.signInHealth")}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6 lg:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
        <div className="flex flex-col gap-2">
          {loading ? (
            <Skeleton className="h-14 w-32" />
          ) : (
            <p className="text-5xl font-semibold leading-none">
              {rate !== null ? `${rate}%` : "—"}
            </p>
          )}
          {rateDelta != null && !loading ? (
            <p className="flex items-center gap-1 text-sm text-muted-foreground">
              {rateDelta >= 0 ? (
                <TrendingUp className="size-4" />
              ) : (
                <TrendingDown className="size-4" />
              )}
              <span className="tabular-nums">
                {rateDelta >= 0 ? "+" : ""}
                {rateDelta}
              </span>
              {t("dashboard.pointsVsPrevious")}
            </p>
          ) : null}
          {loading ? null : (
            <dl className="mt-2 flex flex-col gap-1 text-sm">
              <div className="flex items-baseline justify-between gap-3">
                <dt className="text-muted-foreground">
                  {t("dashboard.success")}
                </dt>
                <dd className="tabular-nums">{success.toLocaleString(locale)}</dd>
              </div>
              <div className="flex items-baseline justify-between gap-3">
                <dt className="text-muted-foreground">
                  {t("dashboard.failed")}
                </dt>
                <dd className="tabular-nums">{failure.toLocaleString(locale)}</dd>
              </div>
            </dl>
          )}
        </div>

        {loading ? (
          <Skeleton className="h-[200px] w-full" />
        ) : hasData ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className="aspect-auto h-[200px] w-full"
          >
            <BarChart data={series} margin={{ left: 12, right: 12, top: 8 }}>
              <CartesianGrid vertical={false} />
              <XAxis
                dataKey="day"
                tickLine={false}
                axisLine={false}
                tickMargin={8}
                minTickGap={24}
                tickFormatter={(day: string) => formatBucket(day)}
              />
              <YAxis hide />
              <ChartTooltip
                content={<ChartTooltipContent indicator="dot" />}
                labelFormatter={(day) =>
                  typeof day === "string" ? formatBucket(day, "long") : day
                }
              />
              {/* 2px surface gap between the stacked fills, rather than a border
                  drawn around each mark. */}
              <Bar
                dataKey="success"
                stackId="outcome"
                fill="var(--color-success)"
                stroke="var(--card)"
                strokeWidth={2}
              />
              <Bar
                dataKey="failure"
                stackId="outcome"
                fill="var(--color-failure)"
                stroke="var(--card)"
                strokeWidth={2}
                radius={[4, 4, 0, 0]}
              />
              <ChartLegend content={<ChartLegendContent />} />
            </BarChart>
          </ChartContainer>
        ) : (
          <p className="self-center text-sm text-muted-foreground">
            {t("dashboard.noData")}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
