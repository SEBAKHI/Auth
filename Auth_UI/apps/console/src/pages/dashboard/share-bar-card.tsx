import { useTranslation } from "react-i18next"
import { Bar, BarChart, XAxis, YAxis } from "recharts"

import { ChartCard } from "@astoom/ui/common/chart-card"
import { Badge } from "@astoom/ui/badge"
import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
} from "@astoom/ui/chart"
import type { ChartConfig } from "@astoom/ui/chart"
import { numberLocale } from "@astoom/ui/format"

import { ORDINAL } from "./chart-constants"

/** One class of an ordered part-to-whole split. */
export type ShareSlice = { key: string; label: string; value: number }

/**
 * Part-to-whole across a small set of **ordered** classes, as a single horizontal
 * stacked bar with a legend and direct percentages.
 *
 * Used for the account-status mix, which replaced a donut. The classes here have a
 * real order (active → pending → locked → inactive), so the preset's sequential
 * ramp is legitimate: the ramp's own order matches the data's. Slices are drawn in
 * that fixed class order, never re-sorted by size, so a class keeps its step as the
 * numbers move.
 */
export function ShareBarCard({
  title,
  description,
  data,
  loading,
  refetching,
  exportName,
  className,
}: {
  title: string
  description?: string
  data: ShareSlice[]
  loading: boolean
  refetching?: boolean
  exportName: string
  className?: string
}) {
  const { t } = useTranslation()
  const locale = numberLocale()

  const slices = data.filter((slice) => slice.value > 0)
  const total = slices.reduce((sum, slice) => sum + slice.value, 0)

  // One row holding every class as its own stacked segment.
  const row = Object.fromEntries(slices.map((s) => [s.key, s.value]))

  const config = Object.fromEntries(
    slices.map((slice, index) => [
      slice.key,
      { label: slice.label, color: ORDINAL[index % ORDINAL.length] },
    ])
  ) satisfies ChartConfig

  const share = (value: number) =>
    total > 0 ? Math.round((value / total) * 100) : 0

  return (
    <ChartCard
      title={title}
      description={description}
      loading={loading}
      refetching={refetching}
      rows={slices}
      exportName={exportName}
      className={className}
      columns={[
        { label: t("dashboard.category"), getValue: (r) => (r as ShareSlice).label },
        {
          label: t("dashboard.count"),
          numeric: true,
          getValue: (r) => (r as ShareSlice).value,
        },
        {
          label: t("dashboard.share"),
          numeric: true,
          getValue: (r) => `${share((r as ShareSlice).value)}%`,
        },
      ]}
    >
      <div className="flex flex-col gap-4">
        <ChartContainer
          config={config}
          dir="ltr"
          className="aspect-auto h-14 w-full"
        >
          <BarChart data={[row]} layout="vertical" margin={{ top: 0, bottom: 0 }}>
            <XAxis type="number" hide />
            <YAxis type="category" hide />
            <ChartTooltip
              cursor={false}
              content={<ChartTooltipContent hideLabel />}
            />
            {slices.map((slice, index) => (
              <Bar
                key={slice.key}
                dataKey={slice.key}
                stackId="share"
                fill={`var(--color-${slice.key})`}
                stroke="var(--card)"
                strokeWidth={2}
                radius={
                  index === 0
                    ? [4, 0, 0, 4]
                    : index === slices.length - 1
                      ? [0, 4, 4, 0]
                      : 0
                }
              />
            ))}
          </BarChart>
        </ChartContainer>

        {/* A legend is always present for >= 2 series, and it doubles as the
            direct-label layer so identity is never colour-alone. */}
        <ul className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
          {slices.map((slice, index) => (
            <li key={slice.key} className="flex items-center gap-2">
              <span
                aria-hidden
                className="size-2.5 shrink-0 rounded-[2px]"
                style={{ background: ORDINAL[index % ORDINAL.length] }}
              />
              <span className="text-muted-foreground">{slice.label}</span>
              <Badge variant="secondary" className="tabular-nums">
                {slice.value.toLocaleString(locale)} · {share(slice.value)}%
              </Badge>
            </li>
          ))}
        </ul>
      </div>
    </ChartCard>
  )
}
