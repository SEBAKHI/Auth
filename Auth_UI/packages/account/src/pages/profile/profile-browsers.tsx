import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ChevronDown,
  Globe,
  Monitor,
  Smartphone,
  Tablet,
  TriangleAlert,
} from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@authsystem/ui/collapsible"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemTitle,
} from "@authsystem/ui/item"
import { Separator } from "@authsystem/ui/separator"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Spinner } from "@authsystem/ui/spinner"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { getErrorMessage } from "@authsystem/api/errors"
import { formatDateTime, formatRelative } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"
import { groupSessionsByBrowser } from "./browser-groups"

type Device = Schemas["KnownDeviceDto"]
type Session = Schemas["SessionDto"]
type DeviceType = Schemas["DeviceType"]

/**
 * The server sends the form factor as one of these four names, which are also
 * the `profile.deviceType.*` translation keys — so the label is a direct lookup
 * rather than a mapping that could fall out of step.
 */
const DEVICE_ICONS: Record<DeviceType, typeof Monitor> = {
  desktop: Monitor,
  mobile: Smartphone,
  tablet: Tablet,
  unknown: Globe,
}

function deviceTypeOf(value: DeviceType | undefined): DeviceType {
  return value ?? "unknown"
}

/** Relative time with the exact instant on hover. */
function RelativeTime({ value }: { value: string | undefined }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span>{formatRelative(value)}</span>
      </TooltipTrigger>
      <TooltipContent>{formatDateTime(value)}</TooltipContent>
    </Tooltip>
  )
}

/** One live session, shown nested under the browser that started it. */
function SessionRow({
  session,
  onRevoke,
  pending,
}: {
  session: Session
  onRevoke: (id: string) => void
  pending: boolean
}) {
  const { t } = useTranslation()

  return (
    <Item variant="muted" size="sm">
      <ItemContent>
        <ItemTitle>
          {session.location ?? session.ipAddress ?? "—"}
          {session.isCurrent ? (
            <Badge variant="outline">{t("profile.currentSession")}</Badge>
          ) : null}
        </ItemTitle>
        <ItemDescription>
          {session.location ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <span dir="ltr">{session.ipAddress ?? "—"}</span>
              </TooltipTrigger>
              <TooltipContent>{t("profile.approximateLocation")}</TooltipContent>
            </Tooltip>
          ) : null}{" "}
          <RelativeTime value={session.lastActivityAt} />
        </ItemDescription>
      </ItemContent>
      <ItemActions>
        {/* The session you are reading this from carries no button. Routing it
            through the revoke endpoint would tear down the credential under the
            page, and the ordinary sign-out already exists elsewhere — the badge
            is what explains the absence. Cloudflare and GitLab both document
            refusing this for the same reason.

            Destructive styling on the rest: revoking ends access and cannot be
            undone. The preset's destructive variant is a tint rather than a
            solid fill, so it reads as dangerous without shouting once per row. */}
        {session.isCurrent ? null : (
          <Button
            variant="destructive"
            size="sm"
            onClick={() => session.id && onRevoke(session.id)}
            disabled={pending}
          >
            {pending ? <Spinner data-icon="inline-start" /> : null}
            {t("profile.revokeSession")}
          </Button>
        )}
      </ItemActions>
    </Item>
  )
}

/** A browser, expanding to reveal the sessions it still holds. */
function BrowserRow({
  device,
  sessions,
  onForget,
  onRevoke,
  forgetting,
  revokingId,
}: {
  device: Device
  sessions: Session[]
  onForget: (device: Device) => void
  onRevoke: (id: string) => void
  forgetting: boolean
  revokingId: string | null
}) {
  const { t } = useTranslation()
  const deviceType = deviceTypeOf(device.deviceType)
  const Icon = DEVICE_ICONS[deviceType]
  const label = device.deviceName ?? t("profile.unknownBrowser")

  return (
    <Collapsible className="group/collapsible">
      <Item variant="outline">
        <ItemMedia variant="icon">
          <Icon />
        </ItemMedia>
        <ItemContent>
          <ItemTitle>
            {label}
            {device.isCurrent ? (
              <Badge variant="outline">{t("profile.currentSession")}</Badge>
            ) : null}
          </ItemTitle>
          <ItemDescription>
            {t(`profile.deviceType.${deviceType}`)}
            {" · "}
            {/* Deliberately not named `count`: that key triggers i18next's
                plural resolution, which would need a different set of keys per
                language and break the locale parity test on Arabic's six
                forms. */}
            {t("profile.activeSessionsCount", {
              n: device.activeSessionCount ?? 0,
            })}
            {" · "}
            {t("profile.firstSeen", { date: formatDateTime(device.firstSeenAt) })}
          </ItemDescription>
        </ItemContent>
        <ItemActions>
          {sessions.length > 0 ? (
            <CollapsibleTrigger asChild>
              <Button variant="ghost" size="sm">
                {t("profile.showSessions")}
                <ChevronDown
                  data-icon="inline-end"
                  className="transition-transform group-data-[state=open]/collapsible:rotate-180"
                />
              </Button>
            </CollapsibleTrigger>
          ) : null}
          {device.isCurrent ? null : (
            <Button
              variant="destructive"
              size="sm"
              onClick={() => onForget(device)}
              disabled={forgetting}
            >
              {forgetting ? <Spinner data-icon="inline-start" /> : null}
              {t("profile.forgetBrowser")}
            </Button>
          )}
        </ItemActions>
      </Item>
      <CollapsibleContent>
        <div className="ms-6 mt-2">
          <ItemGroup>
            {sessions.map((session) => (
              <SessionRow
                key={session.id}
                session={session}
                onRevoke={onRevoke}
                pending={revokingId === session.id}
              />
            ))}
          </ItemGroup>
        </div>
      </CollapsibleContent>
    </Collapsible>
  )
}

export function ProfileBrowsers() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [revokeAllOpen, setRevokeAllOpen] = React.useState(false)
  const [forgetTarget, setForgetTarget] = React.useState<Device | null>(null)
  // Per-row, so revoking one session does not disable every other row's button.
  const [revokingId, setRevokingId] = React.useState<string | null>(null)

  const devicesQuery = useQuery({
    queryKey: ["known-devices"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/devices")),
  })

  const sessionsQuery = useQuery({
    queryKey: ["sessions"],
    queryFn: () => unwrap(api.GET("/api/v1/Auth/sessions")),
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["known-devices"] })
    void queryClient.invalidateQueries({ queryKey: ["sessions"] })
  }

  const revokeOne = useMutation({
    mutationFn: async (sessionId: string) => {
      setRevokingId(sessionId)
      const { error } = await api.DELETE("/api/v1/Auth/sessions/{sessionId}", {
        params: { path: { sessionId } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("profile.sessionRevoked"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
    onSettled: () => setRevokingId(null),
  })

  const revokeAll = useMutation({
    mutationFn: async () => {
      const { error } = await api.DELETE("/api/v1/Auth/sessions")
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("profile.allSessionsRevoked"))
      setRevokeAllOpen(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const forget = useMutation({
    mutationFn: async (deviceId: string) => {
      const { error } = await api.DELETE("/api/v1/Auth/devices/{deviceId}", {
        params: { path: { deviceId } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("profile.browserForgotten"))
      setForgetTarget(null)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const devices = React.useMemo(
    () => devicesQuery.data ?? [],
    [devicesQuery.data]
  )
  const sessions = React.useMemo(
    () => sessionsQuery.data ?? [],
    [sessionsQuery.data]
  )
  const isLoading = devicesQuery.isLoading || sessionsQuery.isLoading
  const isError = devicesQuery.isError || sessionsQuery.isError

  const { groups, unattributed } = React.useMemo(
    () => groupSessionsByBrowser(devices, sessions),
    [devices, sessions]
  )

  const retry = () => {
    void devicesQuery.refetch()
    void sessionsQuery.refetch()
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div className="min-w-0">
          <CardTitle>{t("profile.browsers")}</CardTitle>
          <CardDescription>{t("profile.browsersSubtitle")}</CardDescription>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setRevokeAllOpen(true)}
          disabled={revokeAll.isPending || sessions.length <= 1}
        >
          {revokeAll.isPending ? <Spinner data-icon="inline-start" /> : null}
          {t("profile.revokeAll")}
        </Button>
      </CardHeader>

      <CardContent>
        {isLoading ? (
          <ItemGroup>
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-16 w-full" />
            ))}
          </ItemGroup>
        ) : isError ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <TriangleAlert />
              </EmptyMedia>
              <EmptyTitle>{t("profile.loadFailed")}</EmptyTitle>
              <EmptyDescription>{t("profile.loadFailedBody")}</EmptyDescription>
            </EmptyHeader>
            <Button variant="outline" size="sm" onClick={retry}>
              {t("profile.retry")}
            </Button>
          </Empty>
        ) : groups.length === 0 && unattributed.length === 0 ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <Monitor />
              </EmptyMedia>
              <EmptyTitle>{t("profile.noBrowsers")}</EmptyTitle>
              <EmptyDescription>{t("profile.noBrowsersBody")}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <ItemGroup>
            {groups.map(({ device, sessions: deviceSessions }) => (
              <BrowserRow
                key={device.id}
                device={device}
                sessions={deviceSessions}
                onForget={setForgetTarget}
                onRevoke={(id) => revokeOne.mutate(id)}
                forgetting={
                  forget.isPending && forgetTarget?.id === device.id
                }
                revokingId={revokingId}
              />
            ))}

            {unattributed.length > 0 ? (
              <>
                {groups.length > 0 ? <Separator /> : null}
                <div>
                  <p className="text-sm font-medium">
                    {t("profile.unattributedSessions")}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {t("profile.unattributedHelp")}
                  </p>
                </div>
                <ItemGroup>
                  {unattributed.map((session) => (
                    <SessionRow
                      key={session.id}
                      session={session}
                      onRevoke={(id) => revokeOne.mutate(id)}
                      pending={revokingId === session.id}
                    />
                  ))}
                </ItemGroup>
              </>
            ) : null}
          </ItemGroup>
        )}

        {!isLoading && !isError && groups.length > 0 ? (
          <p className="mt-4 text-xs text-muted-foreground">
            {t("profile.groupingCaveat")}
          </p>
        ) : null}
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

      <ConfirmDialog
        open={forgetTarget !== null}
        onOpenChange={(open) => !open && setForgetTarget(null)}
        title={t("profile.forgetBrowserTitle")}
        description={t("profile.forgetBrowserBody", {
          name: forgetTarget?.deviceName ?? t("profile.unknownBrowser"),
        })}
        confirmLabel={t("profile.forgetBrowser")}
        destructive
        loading={forget.isPending}
        onConfirm={() => forgetTarget?.id && forget.mutate(forgetTarget.id)}
      />
    </Card>
  )
}
