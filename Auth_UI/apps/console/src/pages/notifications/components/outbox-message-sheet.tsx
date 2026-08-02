import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { RotateCcw } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import { directionForLanguage } from "@authsystem/i18n"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@authsystem/ui/sheet"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { formatDateTime } from "@authsystem/ui/format"

/**
 * One delivery-log entry: metadata (status, attempts, errors, audit references)
 * plus the exact rendered content that was — or will be — delivered, shown in a
 * fully sandboxed iframe.
 */
export function OutboxMessageSheet({
  messageId,
  open,
  onOpenChange,
  canManage,
}: {
  messageId: string
  open: boolean
  onOpenChange: (open: boolean) => void
  canManage: boolean
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [mode, setMode] = React.useState<"html" | "text">("html")

  const query = useQuery({
    queryKey: ["notification-outbox-message", messageId],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-outbox/{id}", {
          params: { path: { id: messageId } },
        })
      ),
    enabled: open,
  })
  const message = query.data

  const retryMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-outbox/{id}/retry", {
          params: { path: { id: messageId } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.outboxRetried"))
      void queryClient.invalidateQueries({ queryKey: ["notification-outbox"] })
      void queryClient.invalidateQueries({
        queryKey: ["notification-outbox-message", messageId],
      })
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const retryable = message?.status === "Retry" || message?.status === "Dead"

  const fields: Array<[string, React.ReactNode]> = message
    ? [
        [t("notifications.type"), <span dir="ltr">{message.notificationTypeCode}</span>],
        [
          t("notifications.application"),
          message.applicationName ?? t("notifications.global"),
        ],
        [t("notifications.recipient"), <span dir="ltr">{message.recipient}</span>],
        [
          t("notifications.language"),
          <span dir="ltr" className="uppercase">{message.languageCode}</span>,
        ],
        [
          t("notifications.status"),
          <Badge variant={message.status === "Dead" ? "destructive" : "outline"}>
            {message.status}
          </Badge>,
        ],
        [t("notifications.attemptsLabel"), toNumber(message.attemptCount)],
        [t("common.createdAt"), formatDateTime(message.createdAt)],
        [
          t("notifications.sentAt"),
          message.sentAt ? formatDateTime(message.sentAt) : "—",
        ],
        [
          t("notifications.templateVersionRef"),
          message.templateVersionNumber != null
            ? t("notifications.versionLabel", { version: message.templateVersionNumber })
            : "—",
        ],
      ]
    : []

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent size="xl" className="overflow-y-auto">
        <SheetHeader>
          {/* SheetTitle renders an <h2>: a `dir` on it would re-resolve the
              inherited `text-align: start` and shove the heading to the far edge.
              The subject belongs to the message's own locale, so isolate that run
              inline instead. */}
          <SheetTitle>
            {message?.subject ? (
              <bdi dir={directionForLanguage(message.languageCode ?? "")}>
                {message.subject}
              </bdi>
            ) : (
              t("notifications.outboxMessage")
            )}
          </SheetTitle>
          <SheetDescription>{t("notifications.outboxMessageHint")}</SheetDescription>
        </SheetHeader>

        <div className="flex flex-col gap-4 px-4 pb-6">
          {query.isLoading || !message ? (
            <Skeleton className="h-64 w-full" />
          ) : (
            <>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                {fields.map(([label, value]) => (
                  <React.Fragment key={label}>
                    <dt className="text-muted-foreground">{label}</dt>
                    <dd className="min-w-0 truncate">{value}</dd>
                  </React.Fragment>
                ))}
              </dl>

              {message.lastError ? (
                <p className="rounded-md border border-destructive/50 p-3 text-sm text-destructive" role="alert">
                  {/* Provider errors arrive in English; isolating them inline keeps
                      the paragraph aligned with the rest of the sheet, which a
                      `dir` on the `p` itself would not. */}
                  <bdi dir="auto">{message.lastError}</bdi>
                </p>
              ) : null}

              {canManage && retryable ? (
                <Button
                  variant="outline"
                  size="sm"
                  className="self-start"
                  disabled={retryMutation.isPending}
                  onClick={() => retryMutation.mutate()}
                >
                  <RotateCcw data-icon="inline-start" />
                  {t("notifications.retryNow")}
                </Button>
              ) : null}

              <Tabs value={mode} onValueChange={(value) => setMode(value as "html" | "text")}>
                <TabsList>
                  <TabsTrigger value="html">{t("notifications.htmlTab")}</TabsTrigger>
                  <TabsTrigger value="text">{t("notifications.textTab")}</TabsTrigger>
                </TabsList>
              </Tabs>

              {mode === "html" ? (
                <iframe
                  title={t("notifications.preview")}
                  sandbox=""
                  srcDoc={message.bodyHtml ?? ""}
                  className="h-[480px] w-full rounded-md border bg-white"
                />
              ) : (
                <pre
                  // The delivered body is in one known locale — the message says
                  // which — so bind the direction instead of guessing from the
                  // text, which mis-set it whenever the body opened with Latin.
                  dir={directionForLanguage(message.languageCode ?? "")}
                  className="h-[480px] w-full overflow-auto whitespace-pre-wrap rounded-md border bg-background p-4 text-sm"
                >
                  {message.bodyText || "—"}
                </pre>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
