import * as React from "react"
import { BarChart3, Download, TableIcon } from "lucide-react"
import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@astoom/ui/empty"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import { ToggleGroup, ToggleGroupItem } from "@astoom/ui/toggle-group"
import { Tooltip, TooltipContent, TooltipTrigger } from "@astoom/ui/tooltip"
import { Button } from "@astoom/ui/button"
import { exportRowsToCsv, type ExportColumn } from "@astoom/ui/data-table/csv"
import { formatFieldValue } from "@astoom/ui/data-table/field-format"
import { cn } from "@astoom/ui/utils"

/** A column of the card's table view; doubles as the CSV export column. */
export interface ChartCardColumn extends ExportColumn {
  /** Numeric columns align to the inline end and use tabular figures. */
  numeric?: boolean
}

/**
 * The shell every dashboard chart sits in, so that behaviour required of *all*
 * charts is implemented once:
 *
 * - **A table view twin.** Colour and position are not readable by everyone; each
 *   card can be switched to the same numbers as a table, which is also what makes
 *   the values reachable by a screen reader.
 * - **CSV export** of exactly those rows, reusing the data-table exporter.
 * - **No skeleton flash on refetch.** A skeleton is shown only on first load;
 *   changing the dashboard's time range holds the previous render at reduced
 *   opacity instead, so the page does not jump.
 * - **`Empty` for no data**, rather than a bare centred paragraph.
 *
 * `children` is the chart itself. Give its container a height that includes the
 * x-axis band — sizing to the plot alone clips the tick labels.
 */
export function ChartCard({
  title,
  description,
  children,
  rows,
  columns,
  exportName,
  loading = false,
  refetching = false,
  action,
  className,
  contentClassName,
}: {
  title: string
  description?: string
  children: React.ReactNode
  /** The rows behind the chart, powering the table view and the export. */
  rows: unknown[]
  columns: ChartCardColumn[]
  /** Base file name for the CSV; the exporter appends the date. */
  exportName: string
  loading?: boolean
  refetching?: boolean
  /** Extra header control (e.g. an inner scope switch). */
  action?: React.ReactNode
  className?: string
  contentClassName?: string
}) {
  const { t } = useTranslation()
  const [view, setView] = React.useState<"chart" | "table">("chart")

  const hasData = rows.length > 0

  return (
    <Card className={className}>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-1.5">
          <CardTitle>{title}</CardTitle>
          {description ? (
            <CardDescription>{description}</CardDescription>
          ) : null}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {action}
          {hasData ? (
            <>
              <ToggleGroup
                type="single"
                spacing={0}
                variant="outline"
                size="sm"
                value={view}
                onValueChange={(next) => {
                  if (next === "chart" || next === "table") setView(next)
                }}
                aria-label={t("dashboard.viewAs")}
              >
                <ToggleGroupItem value="chart" aria-label={t("dashboard.viewChart")}>
                  <BarChart3 />
                </ToggleGroupItem>
                <ToggleGroupItem value="table" aria-label={t("dashboard.viewTable")}>
                  <TableIcon />
                </ToggleGroupItem>
              </ToggleGroup>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    type="button"
                    size="icon-sm"
                    variant="ghost"
                    aria-label={t("common.export")}
                    onClick={() =>
                      exportRowsToCsv(rows, columns, exportName, t)
                    }
                  >
                    <Download />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{t("common.export")}</TooltipContent>
              </Tooltip>
            </>
          ) : null}
        </div>
      </CardHeader>
      <CardContent
        className={cn(
          refetching && "opacity-60 transition-opacity",
          contentClassName
        )}
      >
        {loading ? (
          <Skeleton className="h-64 w-full" />
        ) : !hasData ? (
          <Empty className="py-8">
            <EmptyHeader>
              <EmptyTitle>{t("dashboard.noData")}</EmptyTitle>
              <EmptyDescription>{t("dashboard.noDataHint")}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : view === "table" ? (
          <ChartCardTable rows={rows} columns={columns} />
        ) : (
          children
        )}
      </CardContent>
    </Card>
  )
}

function ChartCardTable({
  rows,
  columns,
}: {
  rows: unknown[]
  columns: ChartCardColumn[]
}) {
  const { t } = useTranslation()

  return (
    <div className="max-h-72 overflow-auto">
      <Table>
        <TableHeader>
          <TableRow>
            {columns.map((column) => (
              <TableHead
                key={column.label}
                className={cn(column.numeric && "text-end")}
              >
                {column.label}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row, index) => (
            <TableRow key={index}>
              {columns.map((column) => (
                <TableCell
                  key={column.label}
                  className={cn(column.numeric && "text-end tabular-nums")}
                >
                  {formatFieldValue(column.getValue(row, index), t)}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
