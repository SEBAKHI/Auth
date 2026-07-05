import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { toNumber } from "@/lib/api/helpers"
import type { Schemas } from "@/lib/api/types"

import { ChartEmpty } from "./chart-empty"

type ApplicationActivity = Schemas["ApplicationActivityDto"]

/**
 * Per-application window activity: successful logins, distinct users and live
 * sessions. The row without an application is activity carrying no app context.
 */
export function AppActivityTableCard({
  data,
  loading,
}: {
  data: ApplicationActivity[]
  loading: boolean
}) {
  const { t } = useTranslation()
  const rows = [...data].sort(
    (a, b) => toNumber(b.successfulLogins) - toNumber(a.successfulLogins)
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.appActivity")}</CardTitle>
        <CardDescription>{t("dashboard.appActivitySubtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        {loading ? (
          <Skeleton className="h-[180px] w-full" />
        ) : rows.length === 0 ? (
          <ChartEmpty />
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("dashboard.colApplication")}</TableHead>
                  <TableHead className="text-end">
                    {t("dashboard.colLogins")}
                  </TableHead>
                  <TableHead className="text-end">
                    {t("dashboard.colUsers")}
                  </TableHead>
                  <TableHead className="text-end">
                    {t("dashboard.colSessions")}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.applicationId ?? "unknown"}>
                    <TableCell className="font-medium">
                      {row.applicationName ?? t("common.unknown")}
                    </TableCell>
                    <TableCell className="text-end tabular-nums">
                      {toNumber(row.successfulLogins)}
                    </TableCell>
                    <TableCell className="text-end tabular-nums">
                      {toNumber(row.distinctUsers)}
                    </TableCell>
                    <TableCell className="text-end tabular-nums">
                      {toNumber(row.activeSessions)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
