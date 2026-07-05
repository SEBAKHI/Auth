import { format, parseISO } from "date-fns"
import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from "recharts"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@/components/ui/chart"
import type { ChartConfig } from "@/components/ui/chart"
import { Skeleton } from "@/components/ui/skeleton"

import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"
import type { CountPoint } from "./helpers"

/**
 * Daily-only area trend. Used for distinct-user series where a weekly sum
 * would over-count (a user active on two days is one user, not two).
 */
export function TrendAreaCard({
  title,
  description,
  seriesLabel,
  data,
  loading,
}: {
  title: string
  description: string
  seriesLabel: string
  data: CountPoint[]
  loading: boolean
}) {
  const hasData = data.some((p) => p.count > 0)

  const config = {
    count: { label: seriesLabel, color: SERIES.primary },
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
            <AreaChart data={data} margin={{ left: 12, right: 12, top: 8 }}>
              <CartesianGrid vertical={false} />
              {/* Hidden axis adds top headroom so peaks don't clip against
                  the plot edge. */}
              <YAxis
                hide
                domain={[0, (dataMax: number) => Math.max(1, Math.ceil(dataMax * 1.25))]}
              />
              <XAxis
                dataKey="day"
                tickLine={false}
                axisLine={false}
                tickMargin={8}
                minTickGap={24}
                tickFormatter={(day: string) => format(parseISO(day), "MMM d")}
              />
              <ChartTooltip
                cursor={false}
                content={<ChartTooltipContent indicator="dot" />}
                labelFormatter={(day) =>
                  typeof day === "string"
                    ? format(parseISO(day), "dd MMM yyyy")
                    : day
                }
              />
              <Area
                dataKey="count"
                type="monotone"
                stroke="var(--color-count)"
                fill="var(--color-count)"
                fillOpacity={0.25}
              />
            </AreaChart>
          </ChartContainer>
        ) : (
          <ChartEmpty />
        )}
      </CardContent>
    </Card>
  )
}
