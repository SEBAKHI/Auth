import { useQuery } from "@tanstack/react-query"
import {
  CircleCheck,
  CircleX,
  Globe,
  History,
  Monitor,
  ShieldAlert,
  Smartphone,
  Tablet,
  TriangleAlert,
} from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import {
  Item,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemTitle,
} from "@authsystem/ui/item"
import { Skeleton } from "@authsystem/ui/skeleton"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { cn } from "@authsystem/ui/utils"
import { formatDateTime, formatRelative } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"

type Attempt = Schemas["LoginAttemptDto"]
type DeviceType = Schemas["DeviceType"]

const DEVICE_ICONS: Record<DeviceType, typeof Monitor> = {
  desktop: Monitor,
  mobile: Smartphone,
  tablet: Tablet,
  unknown: Globe,
}

/**
 * Read-only by design. Verdict controls ("was this you?") and destructive
 * controls belong on different surfaces: this one answers what has been tried
 * against the account, and the browsers card above is where anything gets
 * ended. Not recognising something leads to the password, not to a button here.
 */
function AttemptRow({ attempt }: { attempt: Attempt }) {
  const { t } = useTranslation()
  const deviceType = attempt.deviceType ?? "unknown"
  const DeviceIcon = DEVICE_ICONS[deviceType]

  // Three outcomes, not two. An unfinished entry is a sign-in where the password
  // was accepted and the verification code never followed — which, when it was
  // not the account holder, is the clearest warning this card can carry. It is
  // deliberately not toned down to look like an ordinary success.
  const incomplete = attempt.secondFactorIncomplete === true
  const codeAttempts = Number(attempt.secondFactorAttempts ?? 0)
  const Icon = incomplete ? ShieldAlert : attempt.isSuccess ? CircleCheck : CircleX

  return (
    <Item size="sm">
      <ItemMedia variant="icon">
        <Icon
          className={cn(
            attempt.isSuccess ? "text-muted-foreground" : "text-destructive"
          )}
        />
      </ItemMedia>
      <ItemContent>
        <ItemTitle>
          {incomplete
            ? t("profile.loginIncomplete")
            : attempt.isSuccess
              ? t("profile.loginSucceeded")
              : t("profile.loginFailed")}
          {incomplete ? (
            <span className="text-sm font-normal text-muted-foreground">
              {t("profile.loginIncompleteHint")}
            </span>
          ) : null}
          {!attempt.isSuccess && !incomplete && attempt.failureReason ? (
            <span className="text-sm font-normal text-muted-foreground">
              {/* Passed through as stored: the values written do not match the
                  vocabulary the table documents, so a lookup would hide any
                  reason that did not fit it. */}
              {attempt.failureReason}
            </span>
          ) : null}
        </ItemTitle>
        <ItemDescription className="flex flex-wrap items-center gap-x-2">
          <span className="inline-flex items-center gap-1">
            <DeviceIcon className="size-3.5" />
            {attempt.deviceName ?? t("profile.unknownBrowser")}
          </span>
          {attempt.location ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <span>· {attempt.location}</span>
              </TooltipTrigger>
              <TooltipContent>{t("profile.approximateLocation")}</TooltipContent>
            </Tooltip>
          ) : null}
          {attempt.ipAddress ? (
            <span dir="ltr">· {attempt.ipAddress}</span>
          ) : null}
          <Tooltip>
            <TooltipTrigger asChild>
              <span>· {formatRelative(attempt.attemptedAt)}</span>
            </TooltipTrigger>
            <TooltipContent>{formatDateTime(attempt.attemptedAt)}</TooltipContent>
          </Tooltip>
          {/* The count replaces what used to be one red row per rejected code.
              Worded as a labelled number rather than a sentence, because a
              sentence would need six plural forms in Arabic alone. */}
          {codeAttempts > 0 ? (
            <span>· {t("profile.loginCodeAttempts", { count: codeAttempts })}</span>
          ) : null}
        </ItemDescription>
      </ItemContent>
    </Item>
  )
}

export function ProfileLoginActivity() {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: ["login-history"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Auth/login-history", {
          params: { query: { take: 20 } },
        })
      ),
  })

  const attempts = query.data ?? []

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("profile.loginActivity")}</CardTitle>
        <CardDescription>{t("profile.loginActivitySubtitle")}</CardDescription>
      </CardHeader>

      <CardContent>
        {query.isLoading ? (
          <ItemGroup>
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-12 w-full" />
            ))}
          </ItemGroup>
        ) : query.isError ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <TriangleAlert />
              </EmptyMedia>
              <EmptyTitle>{t("profile.loadFailed")}</EmptyTitle>
              <EmptyDescription>{t("profile.loadFailedBody")}</EmptyDescription>
            </EmptyHeader>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void query.refetch()}
            >
              {t("profile.retry")}
            </Button>
          </Empty>
        ) : attempts.length === 0 ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <History />
              </EmptyMedia>
              <EmptyTitle>{t("profile.noLoginActivity")}</EmptyTitle>
            </EmptyHeader>
          </Empty>
        ) : (
          <ItemGroup>
            {attempts.map((attempt) => (
              <AttemptRow key={attempt.id} attempt={attempt} />
            ))}
          </ItemGroup>
        )}
      </CardContent>

      {attempts.length > 0 ? (
        <CardFooter className="text-sm text-muted-foreground">
          {t("profile.unrecognisedActivity")}
        </CardFooter>
      ) : null}
    </Card>
  )
}
