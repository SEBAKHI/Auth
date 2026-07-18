import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { ApplicationSelect } from "@astoom/ui/common/application-select"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Label } from "@astoom/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"
import type { NotificationTypeDto } from "../lib"

/**
 * Creates a template scoped to (type, application, channel). Leaving the
 * application empty creates the global fallback template used by every
 * application without its own override.
 */
export function CreateTemplateDialog({
  open,
  onOpenChange,
  types,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  types: NotificationTypeDto[]
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [typeId, setTypeId] = React.useState<string | undefined>()
  const [applicationId, setApplicationId] = React.useState<string | undefined>()
  const [defaultLanguage, setDefaultLanguage] = React.useState("en")

  const createMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-templates", {
          body: {
            notificationTypeId: typeId!,
            applicationId: applicationId ?? null,
            // NotificationChannelType is numeric in the OpenAPI schema: 1 = Email.
            channel: 1,
            defaultLanguage,
          },
        })
      ),
    onSuccess: (created) => {
      toast.success(t("notifications.templateCreated"))
      void queryClient.invalidateQueries({ queryKey: ["notification-templates"] })
      onOpenChange(false)
      if (created.id) {
        navigate(`/notification-templates/${created.id}`)
      }
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("notifications.newTemplate")}
      description={t("notifications.newTemplateHint")}
      confirmLabel={t("common.create")}
      loading={createMutation.isPending}
      onConfirm={() => typeId && createMutation.mutate()}
    >
      <div className="space-y-4">
        <div className="space-y-2">
          <Label>{t("notifications.type")}</Label>
          <Select value={typeId} onValueChange={setTypeId}>
            <SelectTrigger>
              <SelectValue placeholder={t("notifications.selectType")} />
            </SelectTrigger>
            <SelectContent>
              {types.map((type) => (
                <SelectItem key={type.id} value={type.id!}>
                  {type.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-2">
          <Label>{t("notifications.application")}</Label>
          <ApplicationSelect
            value={applicationId}
            onChange={setApplicationId}
            allowAll
            placeholder={t("notifications.globalTemplate")}
          />
          <p className="text-xs text-muted-foreground">
            {t("notifications.applicationScopeHint")}
          </p>
        </div>
        <div className="space-y-2">
          <Label>{t("notifications.defaultLanguage")}</Label>
          <Select value={defaultLanguage} onValueChange={setDefaultLanguage}>
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {SUPPORTED_LANGUAGES.map((language) => (
                <SelectItem key={language.code} value={language.code}>
                  {language.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
    </ConfirmDialog>
  )
}
