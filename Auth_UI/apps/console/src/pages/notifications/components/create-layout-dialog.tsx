import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"

/**
 * A starter layout so the admin edits a working document rather than a blank
 * one. Only application-specific layouts are created here — the global layout is
 * seeded and always present as the fallback.
 *
 * The direction carriers are deliberately repeated on every container inside the
 * body. Gmail and most webmail strip `<html>`/`<head>` and replace `<body>` with a
 * `<div>` before grafting the message into their own LTR page, so a `dir` on
 * `<html>` and a `direction` in a `body {}` rule — the obvious places — both reach
 * nothing, and Arabic/Urdu/Persian render with an LTR base. Layouts created from
 * this template live only in the database, so no migration can retrofit them: the
 * starter has to be right on the first save.
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
<body dir="{{ dir }}" style="direction: {{ dir }};">
    <div class="container" dir="{{ dir }}" style="direction: {{ dir }};">
        <div class="card" dir="{{ dir }}" style="direction: {{ dir }};">
{{ content | raw }}
        </div>
        <div class="footer" dir="{{ dir }}" style="direction: {{ dir }}; text-align: center;">
            <p dir="{{ dir }}" style="direction: {{ dir }}; text-align: center;">{{ strings.footer | raw }}</p>
        </div>
    </div>
</body>
</html>`

// The sender name is tenant-supplied: one ending in a neutral ("Company Inc.") would
// otherwise drag the sentence's period out of its run and render as ".Company Inc".
const STARTER_STRINGS = JSON.stringify(
  {
    en: { footer: 'This is an automated message from <span dir="auto">{{ SenderName }}</span>.' },
    ar: { footer: 'هذه رسالة تلقائية من <span dir="auto">{{ SenderName }}</span>.' },
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
          {/* A layout name is console metadata, not localized copy, so it takes
              the console's direction rather than guessing from its own value. */}
          <Input
            id="layout-create-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("notifications.layoutNamePlaceholder")}
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
