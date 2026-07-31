import { useTranslation } from "react-i18next"
import { Bar, BarChart, LabelList, XAxis, YAxis } from "recharts"

import { ChartCard } from "@astoom/ui/common/chart-card"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@astoom/ui/chart"
import type { ChartConfig } from "@astoom/ui/chart"
import { numberLocale } from "@astoom/ui/format"

import { SERIES } from "./chart-constants"

/** One labelled value of a single-measure ranking. */
export type RankedRow = { label: string; value: number }

/** Bar thickness plus its gap, used to size the plot to its content. */
const ROW_HEIGHT = 30
const MIN_HEIGHT = 120

/**
 * Sorted horizontal bars for a **nominal** comparison — failure reasons, session
 * end reasons, top actions, entity types, members per organization.
 *
 * This is the form that replaced the dashboard's donuts. A donut asks the reader
 * to compare angles, which fails as soon as two values are close, and it needs one
 * colour per slice — identity encoding this preset's sequential ramp cannot carry.
 * Sorted bars put the ranking in position instead, so every bar can share one hue,
 * and long category names have somewhere to go.
 *
 * The height grows with the row count rather than being fixed, so the value labels
 * and category ticks are never squeezed or clipped.
 */
export function RankedBarCard({
  title,
  description,
  seriesLabel,
  data,
  loading,
  refetching,
  exportName,
  className,
}: {
  title: string
  description?: string
  seriesLabel: string
  data: RankedRow[]
  loading: boolean
  refetching?: boolean
  exportName: string
  className?: string
}) {
  const { t } = useTranslation()
  const locale = numberLocale()

  const rows = [...data]
    .filter((row) => row.value > 0)
    .sort((a, b) => b.value - a.value)

  const config = {
    value: { label: seriesLabel, color: SERIES.primary },
  } satisfies ChartConfig

  const height = Math.max(MIN_HEIGHT, rows.length * ROW_HEIGHT + 16)

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
        { label: t("dashboard.category"), getValue: (row) => (row as RankedRow).label },
        {
          label: seriesLabel,
          numeric: true,
          getValue: (row) => (row as RankedRow).value,
        },
      ]}
    >
      <ChartContainer
        config={config}
        dir="ltr"
        className="aspect-auto w-full"
        style={{ height }}
      >
        <BarChart data={rows} layout="vertical" margin={{ left: 4, right: 44 }}>
          <XAxis type="number" dataKey="value" hide />
          <YAxis
            type="category"
            dataKey="label"
            tickLine={false}
            axisLine={false}
            width={150}
            tickFormatter={(value: string) =>
              value.length > 22 ? `${value.slice(0, 22)}…` : value
            }
          />
          <ChartTooltip
            cursor={false}
            content={<ChartTooltipContent hideLabel />}
          />
          <Bar dataKey="value" fill="var(--color-value)" radius={4}>
            {/* Direct labels outside the bar end: the value is readable without
                hovering, and cannot be clipped by a short bar. */}
            <LabelList
              dataKey="value"
              position="right"
              className="fill-muted-foreground"
              fontSize={12}
              formatter={(value: unknown) =>
                typeof value === "number" ? value.toLocaleString(locale) : ""
              }
            />
          </Bar>
        </BarChart>
      </ChartContainer>
    </ChartCard>
  )
}
