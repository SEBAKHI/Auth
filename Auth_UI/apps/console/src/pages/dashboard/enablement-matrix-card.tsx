import { useTranslation } from "react-i18next"

import { Badge } from "@astoom/ui/badge"
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
import type { Schemas } from "@astoom/api/types"

import { ChartEmpty } from "./chart-empty"
import { daysUntil } from "./helpers"

type Enablement = Schemas["OrganizationApplicationEnablementDto"]

/**
 * Organization × application enablement matrix. Cells show the subscription
 * tier (or a check) and flag enablements expiring within 30 days.
 */
export function EnablementMatrixCard({
  data,
  loading,
}: {
  data: Enablement[]
  loading: boolean
}) {
  const { t } = useTranslation()

  const apps = [
    ...new Map(
      data.map((e) => [e.applicationId, e.applicationName ?? "—"])
    ).entries(),
  ].sort((a, b) => (a[1] ?? "").localeCompare(b[1] ?? ""))
  const orgs = [
    ...new Map(
      data.map((e) => [e.organizationId, e.organizationName ?? "—"])
    ).entries(),
  ].sort((a, b) => (a[1] ?? "").localeCompare(b[1] ?? ""))

  const cell = (orgId: unknown, appId: unknown) =>
    data.find((e) => e.organizationId === orgId && e.applicationId === appId)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.enablementMatrix")}</CardTitle>
        <CardDescription>
          {t("dashboard.enablementMatrixSubtitle")}
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
                  <TableHead>{t("dashboard.colOrganization")}</TableHead>
                  {apps.map(([id, name]) => (
                    <TableHead key={String(id)}>{name}</TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {orgs.map(([orgId, orgName]) => (
                  <TableRow key={String(orgId)}>
                    <TableCell className="font-medium">{orgName}</TableCell>
                    {apps.map(([appId]) => {
                      const enablement = cell(orgId, appId)
                      if (!enablement) {
                        return (
                          <TableCell
                            key={String(appId)}
                            className="text-muted-foreground"
                          >
                            —
                          </TableCell>
                        )
                      }
                      const remaining = daysUntil(enablement.expiresAt)
                      const expiring = remaining !== null && remaining <= 30
                      return (
                        <TableCell key={String(appId)}>
                          <Badge variant={expiring ? "destructive" : "secondary"}>
                            {enablement.subscriptionTier ?? "✓"}
                            {expiring
                              ? ` · ${t("dashboard.expiresInDays", { count: Math.max(remaining, 0) })}`
                              : ""}
                          </Badge>
                        </TableCell>
                      )
                    })}
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
