import { TrendingDown, TrendingUp } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Area, AreaChart, YAxis } from "recharts"

import { Card, CardContent, CardHeader, CardTitle } from "@authsystem/ui/card"
import { ChartContainer } from "@authsystem/ui/chart"
import type { ChartConfig } from "@authsystem/ui/chart"
import { Item, ItemMedia } from "@authsystem/ui/item"
import { Skeleton } from "@authsystem/ui/skeleton"
import { numberLocale } from "@authsystem/ui/format"
import { cn } from "@authsystem/ui/utils"

import { SERIES } from "./chart-constants"
import type { CountPoint } from "./helpers"

function formatValue(value: number | string | undefined): string {
  if (value === undefined || value === null) return "—"
  if (typeof value === "string") return value
  return value.toLocaleString(numberLocale())
}

/**
 * A headline number, optionally with a delta and a sparkline of how it got there.
 *
 * The value uses proportional figures, not `tabular-nums`: equal-width digits are
 * for columns that align vertically (table rows, axis ticks) and make a large
 * standalone number look loose. `tabular-nums` stays on the small delta, which
 * does sit in a row of its siblings.
 */
export function StatTile({
  title,
  value,
  icon: Icon,
  loading,
  delta,
  hint,
  series,
  className,
}: {
  title: string
  value: number | string | undefined
  icon: LucideIcon
  loading: boolean
  /** Whole-percent change vs the previous window; omit to hide the chip. */
  delta?: number | null
  /** One muted line of context under the value. */
  hint?: string
  /** Trailing series for the sparkline; omit for a bare tile. */
  series?: CountPoint[]
  className?: string
}) {
  const { t } = useTranslation()

  const config = {
    count: { label: title, color: SERIES.primary },
  } satisfies ChartConfig

  const hasSeries = series && series.some((point) => point.count > 0)

  return (
    <Card className={className}>
      <CardHeader className="pb-2">
        <Item size="xs" className="p-0">
          <ItemMedia
            variant="icon"
            className="size-9 rounded-2xl bg-muted text-muted-foreground"
          >
            <Icon />
          </ItemMedia>
          <CardTitle className="text-sm font-medium text-muted-foreground">
            {title}
          </CardTitle>
        </Item>
      </CardHeader>
      <CardContent className="flex flex-col gap-1">
        {loading ? (
          <Skeleton className="h-8 w-16" />
        ) : (
          <p className="text-3xl font-semibold">{formatValue(value)}</p>
        )}
        {delta != null && !loading ? (
          <p className="flex items-center gap-1 text-xs text-muted-foreground">
            {delta >= 0 ? (
              <TrendingUp className="size-3.5" />
            ) : (
              <TrendingDown className="size-3.5" />
            )}
            <span className="tabular-nums">
              {delta >= 0 ? "+" : ""}
              {delta}%
            </span>
            {t("dashboard.vsPrevious")}
          </p>
        ) : null}
        {hint && !loading ? (
          <p className="text-xs text-muted-foreground">{hint}</p>
        ) : null}
        {hasSeries && !loading ? (
          <ChartContainer
            config={config}
            dir="ltr"
            className={cn("mt-1 aspect-auto h-10 w-full")}
          >
            <AreaChart data={series} margin={{ top: 2, bottom: 0 }}>
              {/* Headroom so the peak does not clip against the plot edge. */}
              <YAxis
                hide
                domain={[
                  0,
                  (dataMax: number) => Math.max(1, Math.ceil(dataMax * 1.15)),
                ]}
              />
              <Area
                dataKey="count"
                type="monotone"
                stroke="var(--color-count)"
                strokeWidth={2}
                fill="var(--color-count)"
                fillOpacity={0.15}
                dot={false}
              />
            </AreaChart>
          </ChartContainer>
        ) : null}
      </CardContent>
    </Card>
  )
}
