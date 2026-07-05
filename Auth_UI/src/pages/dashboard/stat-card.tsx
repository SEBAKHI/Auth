import { TrendingDown, TrendingUp } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"

/** Headline number card with an optional delta chip and a one-line hint. */
export function StatCard({
  title,
  value,
  icon: Icon,
  loading,
  delta,
  hint,
}: {
  title: string
  value: number | string | undefined
  icon: LucideIcon
  loading: boolean
  /** Whole-percent change vs the previous window; omit to hide the chip. */
  delta?: number | null
  /** Muted context line under the value (e.g. component counts). */
  hint?: string
}) {
  const { t } = useTranslation()
  return (
    <Card>
      <CardHeader className="flex flex-row items-center gap-3 pb-2">
        <span className="flex size-9 items-center justify-center rounded-2xl bg-muted text-muted-foreground">
          <Icon className="size-5" />
        </span>
        <CardTitle className="text-sm font-medium text-muted-foreground">
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1">
        {loading ? (
          <Skeleton className="h-8 w-16" />
        ) : (
          <p className="text-3xl font-semibold tabular-nums">{value ?? "—"}</p>
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
      </CardContent>
    </Card>
  )
}
