import { useTranslation } from "react-i18next"
import { Bar, BarChart, XAxis, YAxis } from "recharts"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from "@astoom/ui/chart"
import type { ChartConfig } from "@astoom/ui/chart"
import { Skeleton } from "@astoom/ui/skeleton"

import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"

/** One category row of a success/failure split (an application, an organization…). */
export type OutcomeRow = { label: string; success: number; failure: number }

/**
 * Horizontal stacked success/failure bars, sorted by volume. Colors match the
 * page-wide outcome assignment (success = mid grey, failure = darkest).
 */
export function OutcomeBarsCard({
  title,
  description,
  data,
  loading,
}: {
  title: string
  description: string
  data: OutcomeRow[]
  loading: boolean
}) {
  const { t } = useTranslation()
  const hasData = data.some((r) => r.success > 0 || r.failure > 0)
  const rows = [...data].sort(
    (a, b) => b.success + b.failure - (a.success + a.failure)
  )

  const config = {
    success: { label: t("dashboard.success"), color: SERIES.success },
    failure: { label: t("dashboard.failed"), color: SERIES.failure },
  } satisfies ChartConfig

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent>
        {loading ? (
          <Skeleton className="h-[240px] w-full" />
        ) : hasData ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className="aspect-auto h-[240px] w-full"
          >
            <BarChart
              data={rows}
              layout="vertical"
              margin={{ left: 12, right: 16 }}
            >
              <XAxis type="number" hide />
              <YAxis
                type="category"
                dataKey="label"
                tickLine={false}
                axisLine={false}
                width={140}
                tickFormatter={(value: string) =>
                  value.length > 18 ? `${value.slice(0, 18)}…` : value
                }
              />
              <ChartTooltip
                cursor={false}
                content={<ChartTooltipContent indicator="dot" />}
              />
              <Bar
                dataKey="success"
                stackId="outcome"
                fill="var(--color-success)"
              />
              <Bar
                dataKey="failure"
                stackId="outcome"
                fill="var(--color-failure)"
                radius={[0, 4, 4, 0]}
              />
              <ChartLegend content={<ChartLegendContent />} />
            </BarChart>
          </ChartContainer>
        ) : (
          <ChartEmpty />
        )}
      </CardContent>
    </Card>
  )
}
