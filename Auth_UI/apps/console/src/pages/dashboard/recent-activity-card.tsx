import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Empty, EmptyHeader, EmptyTitle } from "@authsystem/ui/empty"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@authsystem/ui/item"
import { Skeleton } from "@authsystem/ui/skeleton"
import { formatRelative } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"

/** Latest audit events, newest first. */
export function RecentActivityCard({
  logs,
  loading,
}: {
  logs: Schemas["AuditLogDto"][]
  loading: boolean
}) {
  const { t } = useTranslation()

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2">
        <CardTitle>{t("dashboard.recentActivity")}</CardTitle>
        <Button asChild variant="ghost" size="sm">
          <Link to="/audit-logs">{t("dashboard.viewAll")}</Link>
        </Button>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="flex flex-col gap-2">
            {Array.from({ length: 5 }).map((_, index) => (
              <Skeleton key={index} className="h-10 w-full" />
            ))}
          </div>
        ) : logs.length === 0 ? (
          <Empty className="py-8">
            <EmptyHeader>
              <EmptyTitle>{t("dashboard.noActivity")}</EmptyTitle>
            </EmptyHeader>
          </Empty>
        ) : (
          <ItemGroup>
            {logs.map((log) => (
              <Item key={log.id} size="xs" variant="muted">
                <ItemContent>
                  <ItemTitle>{log.action}</ItemTitle>
                  <ItemDescription>
                    {log.userEmail ?? log.userName ?? "—"}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  {log.entityType ? (
                    <Badge variant="outline">{log.entityType}</Badge>
                  ) : null}
                  <span className="text-xs text-muted-foreground">
                    {formatRelative(log.timestamp)}
                  </span>
                </ItemActions>
              </Item>
            ))}
          </ItemGroup>
        )}
      </CardContent>
    </Card>
  )
}
