import { useTranslation } from "react-i18next"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  XAxis,
  YAxis,
} from "recharts"

import { ChartCard } from "@authsystem/ui/common/chart-card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@authsystem/ui/chart"
import type { ChartConfig } from "@authsystem/ui/chart"

import { SERIES } from "./chart-constants"
import { formatBucket } from "./format-bucket"
import type { CountPoint } from "./helpers"

/**
 * A single measure over time.
 *
 * `area` for a continuous quantity that is meaningful between points (active
 * users, events); `bars` for discrete per-period counts (signups). A single series
 * needs no legend — the card title names it.
 */
export function CountSeriesCard({
  title,
  description,
  seriesLabel,
  data,
  variant = "area",
  loading,
  refetching,
  exportName,
  className,
}: {
  title: string
  description?: string
  seriesLabel: string
  data: CountPoint[]
  variant?: "area" | "bars"
  loading: boolean
  refetching?: boolean
  exportName: string
  className?: string
}) {
  const { t } = useTranslation()

  const config = {
    count: { label: seriesLabel, color: SERIES.primary },
  } satisfies ChartConfig

  const rows = data.some((point) => point.count > 0) ? data : []

  const axis = (
    <>
      <CartesianGrid vertical={false} />
      {/* Headroom so a peak never clips against the plot edge. */}
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
        tickFormatter={(day: string) => formatBucket(day)}
      />
      <ChartTooltip
        content={<ChartTooltipContent indicator="dot" />}
        labelFormatter={(day) =>
          typeof day === "string" ? formatBucket(day, "long") : day
        }
      />
    </>
  )

  return (
    <ChartCard
      title={title}
      description={description}
      loading={loading}
      refetching={refetching}
      rows={rows}
      exportName={exportName}
      className={className}
      columns={[
        {
          label: t("dashboard.day"),
          getValue: (row) => formatBucket((row as CountPoint).day, "long"),
        },
        {
          label: seriesLabel,
          numeric: true,
          getValue: (row) => (row as CountPoint).count,
        },
      ]}
    >
      {/* Sized to include the x-axis band, not just the plot. */}
      <ChartContainer
        config={config}
        dir="ltr"
        className="aspect-auto h-[240px] w-full"
      >
        {variant === "bars" ? (
          <BarChart data={rows} margin={{ left: 12, right: 12, top: 8 }}>
            {axis}
            <Bar dataKey="count" fill="var(--color-count)" radius={[4, 4, 0, 0]} />
          </BarChart>
        ) : (
          <AreaChart data={rows} margin={{ left: 12, right: 12, top: 8 }}>
            {axis}
            <Area
              dataKey="count"
              type="monotone"
              stroke="var(--color-count)"
              strokeWidth={2}
              fill="var(--color-count)"
              fillOpacity={0.2}
            />
          </AreaChart>
        )}
      </ChartContainer>
    </ChartCard>
  )
}
