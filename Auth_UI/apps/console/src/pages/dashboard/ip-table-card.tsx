import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import { toNumber } from "@astoom/api/helpers"
import type { Schemas } from "@astoom/api/types"

import { ChartEmpty } from "./chart-empty"

type IpFailure = Schemas["IpFailureCountDto"]

/** IP addresses with the most failed sign-in attempts in the window. */
export function IpTableCard({
  data,
  loading,
}: {
  data: IpFailure[]
  loading: boolean
}) {
  const { t } = useTranslation()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.topFailingIps")}</CardTitle>
        <CardDescription>
          {t("dashboard.topFailingIpsSubtitle")}
        </CardDescription>
      </CardHeader>
      <CardContent>
        {loading ? (
          <Skeleton className="h-[180px] w-full" />
        ) : data.length === 0 ? (
          <ChartEmpty />
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("dashboard.colIp")}</TableHead>
                  <TableHead className="text-end">
                    {t("dashboard.colFailures")}
                  </TableHead>
                  <TableHead className="text-end">
                    {t("dashboard.colUsernames")}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((row) => (
                  <TableRow key={row.ipAddress}>
                    <TableCell className="font-mono text-sm" dir="ltr">
                      {row.ipAddress}
                    </TableCell>
                    <TableCell className="text-end tabular-nums">
                      {toNumber(row.failureCount)}
                    </TableCell>
                    <TableCell className="text-end tabular-nums">
                      {toNumber(row.distinctUsernames)}
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
