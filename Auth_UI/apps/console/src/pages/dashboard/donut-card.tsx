import { Cell, Pie, PieChart } from "recharts"

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

import { PALETTE } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"

/** A single slice of a donut/category breakdown. */
export type Slice = { key: string; label: string; value: number }

/**
 * Donut chart card driven by a category breakdown. Callers must cap slices at
 * the palette length (fold the tail into "Other") — hues are assigned in fixed
 * order and never cycle.
 */
export function DonutCard({
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
          <ChartEmpty />
        )}
      </CardContent>
    </Card>
  )
}
