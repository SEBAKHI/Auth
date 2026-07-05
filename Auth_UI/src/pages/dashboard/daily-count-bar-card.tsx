import { useMemo, useState } from "react"
import { format, parseISO } from "date-fns"
import { useTranslation } from "react-i18next"
import { Bar, BarChart, CartesianGrid, XAxis } from "recharts"

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
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"

import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"
import { rollupWeeklyCounts } from "./helpers"
import type { CountPoint } from "./helpers"

/** Single-measure bars per UTC day with a weekly rollup toggle (sums are valid). */
export function DailyCountBarCard({
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
  const { t } = useTranslation()
  const [grain, setGrain] = useState<"daily" | "weekly">("daily")

  const points = useMemo(
    () => (grain === "weekly" ? rollupWeeklyCounts(data) : data),
    [data, grain]
  )
  const hasData = data.some((p) => p.count > 0)

  const config = {
    count: { label: seriesLabel, color: SERIES.secondary },
  } satisfies ChartConfig

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-3">
        <div className="space-y-1.5">
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </div>
        <Tabs
          value={grain}
          onValueChange={(value) => setGrain(value as "daily" | "weekly")}
        >
          <TabsList>
            <TabsTrigger value="daily">{t("dashboard.daily")}</TabsTrigger>
            <TabsTrigger value="weekly">{t("dashboard.weekly")}</TabsTrigger>
          </TabsList>
        </Tabs>
      </CardHeader>
      <CardContent>
        {loading ? (
          <Skeleton className="h-[220px] w-full" />
        ) : hasData ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className="aspect-auto h-[220px] w-full"
          >
            <BarChart data={points} margin={{ left: 12, right: 12, top: 8 }}>
              <CartesianGrid vertical={false} />
              <XAxis
                dataKey="day"
                tickLine={false}
                axisLine={false}
                tickMargin={8}
                minTickGap={24}
                tickFormatter={(day: string) => format(parseISO(day), "MMM d")}
              />
              <ChartTooltip
                content={<ChartTooltipContent hideLabel={false} />}
                labelFormatter={(day) =>
                  typeof day === "string"
                    ? format(parseISO(day), "dd MMM yyyy")
                    : day
                }
              />
              <Bar dataKey="count" fill="var(--color-count)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ChartContainer>
        ) : (
          <ChartEmpty />
        )}
      </CardContent>
    </Card>
  )
}
