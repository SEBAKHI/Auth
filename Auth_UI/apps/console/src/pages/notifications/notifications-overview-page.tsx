import { useQuery } from "@tanstack/react-query"
import { FileText, Layers, MailCheck, MailWarning, ShieldCheck } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { RecordLink } from "@authsystem/ui/common/record-link"
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { formatDate, formatDateTime } from "@authsystem/ui/format"
import { Skeleton } from "@authsystem/ui/skeleton"
import { PERMISSIONS } from "@/lib/constants"
import {
  notificationLayoutHref,
  notificationTemplateHref,
  policyRevisionHref,
} from "@/lib/record-hrefs"
import { StatTile } from "@/pages/dashboard/stat-tile"
import { NotificationsTabs } from "./components/notifications-tabs"

/**
 * Landing page of the notifications section: what the section is, how much of
 * it exists, what is actually live, and how delivery is going — before drilling
 * into templates, layouts or the delivery log.
 */
export function NotificationsOverviewPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()

  const query = useQuery({
    queryKey: ["notifications-summary"],
    queryFn: () =>
      unwrap(api.GET("/api/v1/notification-templates/summary", {})),
  })

  // The privacy notice is a separate duty with a separate permission, so this
  // section's own guard does not imply it. Unconditional, the query ran for
  // every notifications operator and 403'd, leaving the tile and the card
  // permanently in their error state on an otherwise working page.
  const canReadPolicy = hasPermission(PERMISSIONS.privacyPolicy.read)
  const policyQuery = useQuery({
    queryKey: ["privacy-policy-versions"],
    queryFn: () => unwrap(api.GET("/api/v1/privacy-policy/versions")),
    enabled: canReadPolicy,
  })

  const summary = query.data
  const loading = query.isLoading
  const versions = policyQuery.data ?? []
  const publishedPolicy = versions.find((version) => version.isPublished)

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("notifications.overviewTitle")}
        description={t("notifications.overviewSubtitle")}
      />

      <NotificationsTabs />

      <div
        className={
          canReadPolicy
            ? "grid gap-4 sm:grid-cols-2 xl:grid-cols-5"
            : "grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
        }
      >
        {canReadPolicy ? (
          <StatTile
            title={t("notifications.tabPolicy")}
            value={publishedPolicy?.version ?? "—"}
            icon={ShieldCheck}
            loading={policyQuery.isLoading}
            hint={t("notifications.overviewPolicyHint", {
              count: (publishedPolicy?.languages ?? []).length,
            })}
          />
        ) : null}
        <StatTile
          title={t("notifications.tabTemplates")}
          value={summary?.templates?.total}
          icon={FileText}
          loading={loading}
          hint={t("notifications.overviewTemplatesHint", {
            published: summary?.templates?.published ?? 0,
            drafts: summary?.templates?.drafts ?? 0,
          })}
        />
        <StatTile
          title={t("notifications.tabLayouts")}
          value={summary?.layouts?.total}
          icon={Layers}
          loading={loading}
          hint={t("notifications.overviewLayoutsHint", {
            published: summary?.layouts?.published ?? 0,
          })}
        />
        <StatTile
          title={t("notifications.overviewSent")}
          value={summary?.outbox?.sent}
          icon={MailCheck}
          loading={loading}
          hint={t("notifications.overviewLast24Hours", {
            count: summary?.outbox?.last24Hours ?? 0,
          })}
        />
        <StatTile
          title={t("notifications.overviewFailed")}
          value={summary?.outbox?.failed}
          icon={MailWarning}
          loading={loading}
          hint={t("notifications.overviewPending", {
            count: summary?.outbox?.pending ?? 0,
          })}
        />
      </div>

      {/*
        Hidden rather than shown-and-broken. Without the permission the query is
        disabled, the list would render empty, and the link beside it points
        at a route that now refuses this holder — three ways of saying "nothing
        here" where absence says it once and correctly.
      */}
      {canReadPolicy ? (
      <Card>
        <CardHeader>
          <CardTitle>{t("notifications.overviewPolicy")}</CardTitle>
          <CardAction>
            <Button variant="outline" size="sm" asChild>
              <Link to="/notifications/policy">{t("notifications.overviewViewPolicy")}</Link>
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {policyQuery.isLoading ? (
            <SummaryListSkeleton />
          ) : versions.length ? (
            versions.slice(0, 4).map((version) => (
              <RecordLink
                key={version.id}
                href={policyRevisionHref(version.id)}
                className="flex w-full items-start justify-between gap-3 rounded-lg p-2 text-start hover:bg-muted"
              >
                <span className="flex min-w-0 flex-col gap-0.5">
                  {/* `block` makes this a block box, so a `dir` on it would
                      re-resolve the inherited `text-align: start` and pull the
                      version left of the date line beneath it. */}
                  <span className="block truncate font-medium">
                    <bdi dir="ltr">{version.version}</bdi>
                  </span>
                  {/* Each run gets its own isolate. Concatenating a localized date
                      with a free-text note in one text node let the bidi algorithm
                      reorder across the join: an English note's trailing period
                      jumped to the far edge in Arabic, rendering as
                      ".Initial published policy". `bdi` + `dir="auto"` scopes each
                      run to its own direction. */}
                  <span className="block truncate text-xs text-muted-foreground">
                    <bdi>{formatDate(version.effectiveDateUtc)}</bdi>
                    {version.changeNote ? (
                      <>
                        {" · "}
                        <bdi dir="auto">{version.changeNote}</bdi>
                      </>
                    ) : null}
                  </span>
                </span>
                <span className="flex shrink-0 items-center gap-1">
                  <Badge variant={version.isPublished ? "secondary" : "outline"}>
                    {version.isPublished
                      ? t("notifications.policyPublished")
                      : t("notifications.policyDraft")}
                  </Badge>
                  {version.notifiedAtUtc ? null : (
                    <Badge variant="outline">
                      {t("notifications.policyNotNotified")}
                    </Badge>
                  )}
                </span>
              </RecordLink>
            ))
          ) : (
            <EmptyLine text={t("notifications.overviewNoPolicy")} />
          )}
        </CardContent>
      </Card>
      ) : null}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>{t("notifications.overviewPublishedTemplates")}</CardTitle>
            <CardAction>
              <Button variant="outline" size="sm" asChild>
              <Link to="/notifications/templates">{t("notifications.overviewViewTemplates")}</Link>
            </Button>
            </CardAction>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {loading ? (
              <SummaryListSkeleton />
            ) : summary?.publishedTemplates?.length ? (
              summary.publishedTemplates.map((template) => (
                <RecordLink
                  key={template.id}
                  href={notificationTemplateHref(template.id)}
                  className="flex w-full items-start justify-between gap-3 rounded-lg p-2 text-start hover:bg-muted"
                >
                  <span className="flex min-w-0 flex-col gap-0.5">
                    <span className="block truncate font-medium">
                      {template.typeName}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      <bdi dir="auto">
                        {template.applicationName ?? t("notifications.global")}
                      </bdi>
                      {" · "}
                      <bdi>{template.channel}</bdi>
                      {template.modifiedAt ? (
                        <>
                          {" · "}
                          <bdi>{formatDateTime(template.modifiedAt)}</bdi>
                        </>
                      ) : null}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-1">
                    <Badge variant="secondary">
                      {t("notifications.publishedVersion", {
                        version: template.publishedVersionNumber,
                      })}
                    </Badge>
                    {template.hasUnpublishedDraft ? (
                      <Badge variant="outline">
                        {t("notifications.draft")}
                      </Badge>
                    ) : null}
                  </span>
                </RecordLink>
              ))
            ) : (
              <EmptyLine text={t("notifications.overviewNoPublished")} />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("notifications.overviewLayouts")}</CardTitle>
            <CardAction>
              <Button variant="outline" size="sm" asChild>
              <Link to="/notifications/layouts">{t("notifications.overviewViewLayouts")}</Link>
            </Button>
            </CardAction>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {loading ? (
              <SummaryListSkeleton />
            ) : summary?.publishedLayouts?.length ? (
              summary.publishedLayouts.map((layout) => (
                <RecordLink
                  key={layout.id}
                  href={notificationLayoutHref(layout.id)}
                  className="flex w-full items-start justify-between gap-3 rounded-lg p-2 text-start hover:bg-muted"
                >
                  <span className="flex min-w-0 flex-col gap-0.5">
                    <span className="block truncate font-medium">
                      {layout.name}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      <bdi dir="auto">
                        {layout.applicationName ?? t("notifications.global")}
                      </bdi>
                      {" · "}
                      <bdi>{layout.channel}</bdi>
                      {layout.publishedAt ? (
                        <>
                          {" · "}
                          <bdi>{formatDateTime(layout.publishedAt)}</bdi>
                        </>
                      ) : null}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-1">
                    <Badge variant={layout.isPublished ? "secondary" : "outline"}>
                      {layout.isPublished
                        ? t("notifications.published")
                        : t("notifications.unpublished")}
                    </Badge>
                    {layout.hasUnpublishedChanges ? (
                      <Badge variant="outline">
                        {t("notifications.draft")}
                      </Badge>
                    ) : null}
                  </span>
                </RecordLink>
              ))
            ) : (
              <EmptyLine text={t("notifications.overviewNoLayouts")} />
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

function SummaryListSkeleton() {
  return (
    <div className="flex flex-col gap-2">
      {Array.from({ length: 3 }).map((_, index) => (
        <Skeleton key={index} className="h-10 w-full" />
      ))}
    </div>
  )
}

function EmptyLine({ text }: { text: string }) {
  return (
    <p className="py-4 text-center text-sm text-muted-foreground">{text}</p>
  )
}
