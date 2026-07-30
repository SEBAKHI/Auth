import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Monitor, Smartphone, Tablet } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Spinner } from "@astoom/ui/spinner"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@astoom/ui/card"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@astoom/ui/tooltip"
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import { formatDateTime, formatRelative } from "@astoom/ui/format"
import { parseUserAgent, type DeviceType } from "@astoom/ui/user-agent"
import type { Schemas } from "@astoom/api/types"

const DEVICE_ICONS: Record<DeviceType, typeof Monitor> = {
  desktop: Monitor,
  mobile: Smartphone,
  tablet: Tablet,
}

/** "Chrome on Windows"-style label a non-technical user can read. */
function DeviceCell({ session }: { session: Schemas["SessionDto"] }) {
  const { t } = useTranslation()
  const parsed = parseUserAgent(session.userAgent)
  const Icon = DEVICE_ICONS[parsed.deviceType]

  const browser = parsed.browser ?? t("profile.unknownBrowser")
  const label =
    session.deviceName ??
    (parsed.os ? t("profile.browserOnOs", { browser, os: parsed.os }) : browser)

  return (
    <div className="flex min-w-0 items-center gap-3">
      <Icon className="size-5 shrink-0 text-muted-foreground" />
      <div className="min-w-0">
        <p className="flex items-center gap-2 text-sm font-medium">
          <span className="truncate">{label}</span>
          {session.isCurrent ? (
            <Badge variant="outline">{t("profile.currentSession")}</Badge>
          ) : null}
        </p>
        <p className="truncate text-xs text-muted-foreground">
          {t(`profile.deviceType.${parsed.deviceType}`)}
        </p>
      </div>
    </div>
  )
}

function RelativeTimeCell({ value }: { value: string | undefined }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="text-sm text-muted-foreground">
          {formatRelative(value)}
        </span>
      </TooltipTrigger>
      <TooltipContent>{formatDateTime(value)}</TooltipContent>
    </Tooltip>
  )
}

export function ProfileSessions() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [revokeAllOpen, setRevokeAllOpen] = React.useState(false)

  const query = useQuery({
    queryKey: ["sessions"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/sessions")),
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["sessions"] })

  const revokeOne = useMutation({
    mutationFn: async (sessionId: string) => {
      const { error } = await api.DELETE("/api/v1/Auth/sessions/{sessionId}", {
        params: { path: { sessionId } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("profile.sessionRevoked"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const revokeAll = useMutation({
    mutationFn: async () => {
      const { error } = await api.DELETE("/api/v1/Auth/sessions")
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("profile.allSessionsRevoked"))
      setRevokeAllOpen(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const sessions = query.data ?? []

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="text-base">{t("profile.sessions")}</CardTitle>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setRevokeAllOpen(true)}
          disabled={revokeAll.isPending}
        >
          {revokeAll.isPending ? <Spinner /> : null}
          {t("profile.revokeAll")}
        </Button>
      </CardHeader>
      <CardContent>
        {query.isLoading ? (
          <div className="flex flex-col gap-2">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : sessions.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            {t("common.empty")}
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("profile.device")}</TableHead>
                <TableHead>{t("profile.ipAddress")}</TableHead>
                <TableHead>{t("profile.lastActivity")}</TableHead>
                <TableHead>{t("profile.signedInAt")}</TableHead>
                <TableHead>
                  <span className="sr-only">{t("common.actions")}</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sessions.map((session) => (
                <TableRow key={session.id}>
                  <TableCell className="max-w-72">
                    <DeviceCell session={session} />
                  </TableCell>
                  <TableCell>
                    <div className="text-sm">{session.ipAddress ?? "—"}</div>
                    {session.location ? (
                      <div className="text-xs text-muted-foreground">
                        {session.location}
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell>
                    <RelativeTimeCell value={session.lastActivityAt} />
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDateTime(session.createdAt)}
                  </TableCell>
                  <TableCell className="text-end">
                    {!session.isCurrent ? (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() =>
                          session.id && revokeOne.mutate(session.id)
                        }
                        disabled={revokeOne.isPending}
                      >
                        {t("profile.revokeSession")}
                      </Button>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>

      <ConfirmDialog
        open={revokeAllOpen}
        onOpenChange={setRevokeAllOpen}
        title={t("profile.revokeAllTitle")}
        description={t("profile.revokeAllBody")}
        confirmLabel={t("profile.revokeAll")}
        destructive
        loading={revokeAll.isPending}
        onConfirm={() => revokeAll.mutate()}
      />
    </Card>
  )
}
