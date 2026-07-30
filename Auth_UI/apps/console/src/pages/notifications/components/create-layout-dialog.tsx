import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { ApplicationSelect } from "@astoom/ui/common/application-select"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"

/**
 * A starter layout so the admin edits a working document rather than a blank
 * one. Only application-specific layouts are created here — the global layout is
 * seeded and always present as the fallback.
 */
const STARTER_CONTENT = `<!DOCTYPE html>
<html dir="{{ dir }}" lang="{{ lang }}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif; margin: 0; padding: 0; background-color: #f5f5f5; direction: {{ dir }}; }
        .container { max-width: 600px; margin: 0 auto; padding: 40px 20px; }
        .card { background: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .footer { text-align: center; margin-top: 30px; color: #9ca3af; font-size: 14px; }
    </style>
</head>
<body>
    <div class="container">
        <div class="card">
{{ content | raw }}
        </div>
        <div class="footer">
            <p>{{ strings.footer | raw }}</p>
        </div>
    </div>
</body>
</html>`

const STARTER_STRINGS = JSON.stringify(
  {
    en: { footer: "This is an automated message from {{ SenderName }}." },
    ar: { footer: "هذه رسالة تلقائية من {{ SenderName }}." },
  },
  null,
  2
)

export function CreateLayoutDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [name, setName] = React.useState("")
  const [applicationId, setApplicationId] = React.useState<string | undefined>()

  const createMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-layouts", {
          body: {
            applicationId: applicationId ?? null,
            name,
            draftContent: STARTER_CONTENT,
            draftStringsJson: STARTER_STRINGS,
            // NotificationChannelType is numeric in the schema: 1 = Email.
            channel: 1,
          },
        })
      ),
    onSuccess: (created) => {
      toast.success(t("notifications.layoutCreated"))
      void queryClient.invalidateQueries({ queryKey: ["notification-layouts"] })
      onOpenChange(false)
      if (created.id) {
        navigate(`/notifications/layouts/${created.id}`)
      }
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("notifications.newLayout")}
      description={t("notifications.newLayoutHint")}
      confirmLabel={t("common.create")}
      loading={createMutation.isPending}
      onConfirm={() => name && applicationId && createMutation.mutate()}
    >
      <FieldGroup>
        <Field>
          <FieldLabel htmlFor="layout-create-name">
            {t("notifications.layoutName")}
          </FieldLabel>
          <Input
            id="layout-create-name"
            dir="auto"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </Field>
        <Field>
          <FieldLabel htmlFor="layout-create-application">
            {t("notifications.application")}
          </FieldLabel>
          <ApplicationSelect
            id="layout-create-application"
            value={applicationId}
            onChange={setApplicationId}
            placeholder={t("notifications.selectApplication")}
          />
          <FieldDescription>
            {t("notifications.newLayoutScopeHint")}
          </FieldDescription>
        </Field>
      </FieldGroup>
    </ConfirmDialog>
  )
}
