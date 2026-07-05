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
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from "@/components/ui/chart"
import type { ChartConfig } from "@/components/ui/chart"
import { Skeleton } from "@/components/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"

import { SERIES } from "./chart-constants"
import { ChartEmpty } from "./chart-empty"
import { rollupWeeklyLogins } from "./helpers"
import type { LoginPoint } from "./helpers"

/**
 * Stacked success/failure login bars per UTC day, with a weekly rollup toggle
 * (weekly sums are valid for attempt counts).
 */
export function DailyLoginsCard({
  data,
  loading,
}: {
  data: LoginPoint[]
  loading: boolean
}) {
  const { t } = useTranslation()
  const [grain, setGrain] = useState<"daily" | "weekly">("daily")

  const points = useMemo(
    () => (grain === "weekly" ? rollupWeeklyLogins(data) : data),
    [data, grain]
  )
  const hasData = data.some((p) => p.success > 0 || p.failure > 0)

  const config = {
    success: { label: t("dashboard.success"), color: SERIES.success },
    failure: { label: t("dashboard.failed"), color: SERIES.failure },
  } satisfies ChartConfig

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-3">
        <div className="space-y-1.5">
          <CardTitle>{t("dashboard.loginOutcomes")}</CardTitle>
          <CardDescription>
            {t("dashboard.loginOutcomesSubtitle")}
          </CardDescription>
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
          <Skeleton className="h-[240px] w-full" />
        ) : hasData ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className="aspect-auto h-[240px] w-full"
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
                content={<ChartTooltipContent indicator="dot" />}
                labelFormatter={(day) =>
                  typeof day === "string"
                    ? format(parseISO(day), "dd MMM yyyy")
                    : day
                }
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
                radius={[4, 4, 0, 0]}
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
