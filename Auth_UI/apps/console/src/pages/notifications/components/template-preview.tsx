import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { directionForLanguage } from "@astoom/i18n"
import { Badge } from "@astoom/ui/badge"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import { PreviewPane } from "./preview-pane"
import type { NotificationPreviewDto } from "../lib"

/**
 * Live server-rendered preview: the editor buffer goes through the real Fluid
 * pipeline (sample data + published layout), so what is shown here is exactly
 * what a real send produces.
 */
export function TemplatePreview({
  notificationTypeId,
  applicationId,
  languageCode,
  subject,
  bodyHtml,
  bodyText,
}: {
  notificationTypeId: string
  applicationId?: string | null
  languageCode: string
  subject: string
  bodyHtml: string
  bodyText: string
}) {
  const { t } = useTranslation()
  const [preview, setPreview] = React.useState<NotificationPreviewDto | null>(null)

  // Debounce the buffer so typing does not spam the preview endpoint.
  const debounced = useDebouncedValue(
    React.useMemo(
      () => ({ languageCode, subject, bodyHtml, bodyText }),
      [languageCode, subject, bodyHtml, bodyText]
    ),
    600
  )

  const renderMutation = useMutation({
    mutationFn: (input: { languageCode: string; subject: string; bodyHtml: string; bodyText: string }) =>
      unwrap(
        api.POST("/api/v1/notification-templates/preview", {
          body: {
            notificationTypeId,
            applicationId: applicationId ?? null,
            languageCode: input.languageCode,
            subject: input.subject,
            bodyHtml: input.bodyHtml,
            bodyText: input.bodyText || null,
          },
        })
      ),
    onSuccess: (data) => setPreview(data),
  })

  const render = renderMutation.mutate
  React.useEffect(() => {
    if (debounced.subject || debounced.bodyHtml) {
      render(debounced)
    }
  }, [debounced, render])

  return (
    <div className="flex flex-col gap-3">
      {preview ? (
        <p className="text-sm">
          <span className="text-muted-foreground">{t("notifications.subject")}: </span>
          {/* The rendered subject belongs to the locale being previewed, which the
              DTO names — `auto` guessed it from the text instead. */}
          <span
            className="font-medium"
            dir={directionForLanguage(preview.languageCode ?? languageCode)}
          >
            {preview.subject}
          </span>
          <Badge variant="outline" className="ms-2">
            {preview.direction}
          </Badge>
        </p>
      ) : null}

      <PreviewPane
        preview={preview}
        error={
          renderMutation.isError
            ? renderMutation.error instanceof Error
              ? renderMutation.error.message
              : t("notifications.previewFailed")
            : null
        }
      />
    </div>
  )
}
