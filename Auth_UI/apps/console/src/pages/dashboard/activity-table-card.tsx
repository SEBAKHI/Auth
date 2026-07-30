import * as React from "react"
import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { Badge } from "@astoom/ui/badge"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { Empty, EmptyHeader, EmptyTitle } from "@astoom/ui/empty"
import { numberLocale } from "@astoom/ui/format"

import { SERIES } from "./chart-constants"
import { successRate } from "./helpers"

/** One row of the "where sign-ins happen" table. */
export type ActivityRow = {
  key: string
  label: string
  success: number
  failure: number
  /** Extra column — distinct users for applications, members for organizations. */
  people?: number
  sessions?: number
  inactive?: boolean
}

/**
 * Where sign-ins happen, per application and per organization.
 *
 * Deliberately a table, not more bars. Past roughly seven classes that all carry
 * meaning, bars stop being readable and a table is the right form — and this
 * replaced three separate cards (two outcome-bar charts and an app table) that
 * between them answered one question, split across two page sections.
 *
 * The inline bar is a magnitude cue inside the row, so the ranking is still visible
 * at a glance while the exact numbers stay legible beside it.
 */
export function ActivityTableCard({
  applications,
  organizations,
  loading,
  refetching,
  organizationNote,
}: {
  applications: ActivityRow[]
  organizations: ActivityRow[]
  loading: boolean
  refetching?: boolean
  /** Why the per-organization totals can exceed the raw attempt count. */
  organizationNote: string
}) {
  const { t } = useTranslation()
  const [scope, setScope] = React.useState<"applications" | "organizations">(
    "applications"
  )

  const rows = scope === "applications" ? applications : organizations

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-1.5">
          <CardTitle>{t("dashboard.whereSignIns")}</CardTitle>
          <CardDescription>
            {scope === "organizations"
              ? organizationNote
              : t("dashboard.whereSignInsSubtitle")}
          </CardDescription>
        </div>
        <Tabs
          value={scope}
          onValueChange={(value) =>
            setScope(value as "applications" | "organizations")
          }
        >
          <TabsList>
            <TabsTrigger value="applications">
              {t("dashboard.applications")}
            </TabsTrigger>
            <TabsTrigger value="organizations">
              {t("dashboard.organizations")}
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </CardHeader>
      <CardContent className={refetching ? "opacity-60 transition-opacity" : ""}>
        {loading ? (
          <div className="flex flex-col gap-2">
            {Array.from({ length: 5 }).map((_, index) => (
              <Skeleton key={index} className="h-9 w-full" />
            ))}
          </div>
        ) : (
          <Tabs value={scope}>
            <TabsContent value={scope}>
              <ActivityTable
                rows={rows}
                peopleLabel={
                  scope === "applications"
                    ? t("dashboard.colUsers")
                    : t("dashboard.members")
                }
                showSessions={scope === "applications"}
              />
            </TabsContent>
          </Tabs>
        )}
      </CardContent>
    </Card>
  )
}

function ActivityTable({
  rows,
  peopleLabel,
  showSessions,
}: {
  rows: ActivityRow[]
  peopleLabel: string
  showSessions: boolean
}) {
  const { t } = useTranslation()
  const locale = numberLocale()

  if (rows.length === 0) {
    return (
      <Empty className="py-8">
        <EmptyHeader>
          <EmptyTitle>{t("dashboard.noData")}</EmptyTitle>
        </EmptyHeader>
      </Empty>
    )
  }

  const sorted = [...rows].sort(
    (a, b) => b.success + b.failure - (a.success + a.failure)
  )
  const busiest = Math.max(...sorted.map((row) => row.success + row.failure), 1)

  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t("dashboard.colName")}</TableHead>
            <TableHead>{t("dashboard.colSignIns")}</TableHead>
            <TableHead className="text-end">{t("dashboard.successRate")}</TableHead>
            <TableHead className="text-end">{peopleLabel}</TableHead>
            {showSessions ? (
              <TableHead className="text-end">
                {t("dashboard.colSessions")}
              </TableHead>
            ) : null}
          </TableRow>
        </TableHeader>
        <TableBody>
          {sorted.map((row) => {
            const total = row.success + row.failure
            const rate = successRate(row.success, row.failure)
            return (
              <TableRow key={row.key}>
                <TableCell className="max-w-56">
                  <span className="flex items-center gap-2">
                    <span className="truncate">{row.label}</span>
                    {row.inactive ? (
                      <Badge variant="outline">{t("common.inactive")}</Badge>
                    ) : null}
                  </span>
                </TableCell>
                <TableCell>
                  <span className="flex items-center gap-2">
                    <span
                      aria-hidden
                      className="h-2 min-w-0.5 rounded-full"
                      style={{
                        width: `${Math.max(2, (total / busiest) * 100)}%`,
                        maxWidth: "6rem",
                        background: SERIES.primary,
                      }}
                    />
                    <span className="tabular-nums">
                      {total.toLocaleString(locale)}
                    </span>
                  </span>
                </TableCell>
                <TableCell className="text-end tabular-nums">
                  {rate !== null ? `${rate}%` : "—"}
                </TableCell>
                <TableCell className="text-end tabular-nums">
                  {row.people != null ? row.people.toLocaleString(locale) : "—"}
                </TableCell>
                {showSessions ? (
                  <TableCell className="text-end tabular-nums">
                    {row.sessions != null
                      ? row.sessions.toLocaleString(locale)
                      : "—"}
                  </TableCell>
                ) : null}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
