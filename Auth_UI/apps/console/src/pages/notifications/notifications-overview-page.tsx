import { useQuery } from "@tanstack/react-query"
import { FileText, Layers, MailCheck, MailWarning } from "lucide-react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { PageHeader } from "@astoom/ui/common/page-header"
import { formatDateTime } from "@astoom/ui/format"
import { Skeleton } from "@astoom/ui/skeleton"
import { StatCard } from "@/pages/dashboard/stat-card"
import { NotificationsTabs } from "./components/notifications-tabs"

/**
 * Landing page of the notifications section: what the section is, how much of
 * it exists, what is actually live, and how delivery is going — before drilling
 * into templates, layouts or the delivery log.
 */
export function NotificationsOverviewPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ["notifications-summary"],
    queryFn: () =>
      unwrap(api.GET("/api/v1/notification-templates/summary", {})),
  })

  const summary = query.data
  const loading = query.isLoading

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("notifications.overviewTitle")}
        description={t("notifications.overviewSubtitle")}
      />

      <NotificationsTabs />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          title={t("notifications.tabTemplates")}
          value={summary?.templates?.total}
          icon={FileText}
          loading={loading}
          hint={t("notifications.overviewTemplatesHint", {
            published: summary?.templates?.published ?? 0,
            drafts: summary?.templates?.drafts ?? 0,
          })}
        />
        <StatCard
          title={t("notifications.tabLayouts")}
          value={summary?.layouts?.total}
          icon={Layers}
          loading={loading}
          hint={t("notifications.overviewLayoutsHint", {
            published: summary?.layouts?.published ?? 0,
          })}
        />
        <StatCard
          title={t("notifications.overviewSent")}
          value={summary?.outbox?.sent}
          icon={MailCheck}
          loading={loading}
          hint={t("notifications.overviewLast24Hours", {
            count: summary?.outbox?.last24Hours ?? 0,
          })}
        />
        <StatCard
          title={t("notifications.overviewFailed")}
          value={summary?.outbox?.failed}
          icon={MailWarning}
          loading={loading}
          hint={t("notifications.overviewPending", {
            count: summary?.outbox?.pending ?? 0,
          })}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>{t("notifications.overviewPublishedTemplates")}</CardTitle>
            <CardAction>
              <Button
                variant="outline"
                size="sm"
                onClick={() => navigate("/notifications/templates")}
              >
                {t("notifications.overviewViewTemplates")}
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent className="space-y-3">
            {loading ? (
              <SummaryListSkeleton />
            ) : summary?.publishedTemplates?.length ? (
              summary.publishedTemplates.map((template) => (
                <button
                  key={template.id}
                  type="button"
                  className="flex w-full items-start justify-between gap-3 rounded-lg p-2 text-start hover:bg-muted"
                  onClick={() =>
                    navigate(`/notifications/templates/${template.id}`)
                  }
                >
                  <span className="min-w-0 space-y-0.5">
                    <span className="block truncate font-medium">
                      {template.typeName}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      {template.applicationName ?? t("notifications.global")} ·{" "}
                      {template.channel}
                      {template.modifiedAt
                        ? ` · ${formatDateTime(template.modifiedAt)}`
                        : ""}
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
                </button>
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
              <Button
                variant="outline"
                size="sm"
                onClick={() => navigate("/notifications/layouts")}
              >
                {t("notifications.overviewViewLayouts")}
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent className="space-y-3">
            {loading ? (
              <SummaryListSkeleton />
            ) : summary?.publishedLayouts?.length ? (
              summary.publishedLayouts.map((layout) => (
                <button
                  key={layout.id}
                  type="button"
                  className="flex w-full items-start justify-between gap-3 rounded-lg p-2 text-start hover:bg-muted"
                  onClick={() => navigate(`/notifications/layouts/${layout.id}`)}
                >
                  <span className="min-w-0 space-y-0.5">
                    <span className="block truncate font-medium">
                      {layout.name}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      {layout.applicationName ?? t("notifications.global")} ·{" "}
                      {layout.channel}
                      {layout.publishedAt
                        ? ` · ${formatDateTime(layout.publishedAt)}`
                        : ""}
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
                </button>
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
    <div className="space-y-2">
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
