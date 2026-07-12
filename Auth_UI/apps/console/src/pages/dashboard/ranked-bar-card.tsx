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
  ChartTooltip,
  ChartTooltipContent,
} from "@astoom/ui/chart"
import type { ChartConfig } from "@astoom/ui/chart"
import { Skeleton } from "@astoom/ui/skeleton"

import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"

/** One labeled value of a single-measure ranking. */
export type RankedRow = { label: string; value: number }

/** Horizontal single-hue ranking bars, sorted descending (top-N + "Other" ready). */
export function RankedBarCard({
  title,
  description,
  seriesLabel,
  data,
  loading,
}: {
  title: string
  description: string
  seriesLabel: string
  data: RankedRow[]
  loading: boolean
}) {
  const hasData = data.some((r) => r.value > 0)
  const rows = [...data].sort((a, b) => b.value - a.value)

  const config = {
    value: { label: seriesLabel, color: SERIES.secondary },
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
                content={<ChartTooltipContent hideLabel />}
              />
              <Bar dataKey="value" fill="var(--color-value)" radius={5} />
            </BarChart>
          </ChartContainer>
        ) : (
          <ChartEmpty />
        )}
      </CardContent>
    </Card>
  )
}
