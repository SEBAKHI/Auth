import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { Skeleton } from "@authsystem/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@authsystem/ui/table"
import { toNumber } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

import { Empty, EmptyHeader, EmptyTitle } from "@authsystem/ui/empty"

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
          <Empty className="py-8">
            <EmptyHeader>
              <EmptyTitle>{t("dashboard.noData")}</EmptyTitle>
            </EmptyHeader>
          </Empty>
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
                    {/* `dir` on the cell would re-resolve its inherited
                        `text-align: start` to left and make this column the only
                        one breaking the table's RTL alignment. */}
                    <TableCell className="font-mono text-sm">
                      <bdi dir="ltr">{row.ipAddress}</bdi>
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
