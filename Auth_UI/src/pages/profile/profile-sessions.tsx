import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2, Monitor } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@/components/common/confirm-dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { getErrorMessage } from "@/lib/errors"
import { formatRelative } from "@/lib/format"

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
          {revokeAll.isPending ? <Loader2 className="animate-spin" /> : null}
          {t("profile.revokeAll")}
        </Button>
      </CardHeader>
      <CardContent>
        {query.isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : sessions.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            {t("common.empty")}
          </p>
        ) : (
          <ul className="divide-y">
            {sessions.map((session) => (
              <li
                key={session.id}
                className="flex items-center justify-between gap-3 py-3"
              >
                <div className="flex min-w-0 items-center gap-3">
                  <Monitor className="size-5 shrink-0 text-muted-foreground" />
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 truncate text-sm font-medium">
                      {session.deviceName ?? session.userAgent ?? "—"}
                      {session.isCurrent ? (
                        <Badge variant="outline">
                          {t("profile.currentSession")}
                        </Badge>
                      ) : null}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {session.ipAddress ?? "—"} ·{" "}
                      {formatRelative(session.lastActivityAt)}
                    </p>
                  </div>
                </div>
                {!session.isCurrent ? (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => session.id && revokeOne.mutate(session.id)}
                    disabled={revokeOne.isPending}
                  >
                    {t("profile.revokeSession")}
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
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
