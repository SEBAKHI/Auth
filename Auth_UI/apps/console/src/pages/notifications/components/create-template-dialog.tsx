import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
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
        navigate(`/notifications/templates/${created.id}`)
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
      <FieldGroup>
        <Field>
          <FieldLabel htmlFor="create-template-type">
            {t("notifications.type")}
          </FieldLabel>
          <Select value={typeId} onValueChange={setTypeId}>
            <SelectTrigger id="create-template-type">
              <SelectValue placeholder={t("notifications.selectType")} />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {types.map((type) => (
                  <SelectItem key={type.id} value={type.id!}>
                    {type.name}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="create-template-application">
            {t("notifications.application")}
          </FieldLabel>
          <ApplicationSelect
            id="create-template-application"
            value={applicationId}
            onChange={setApplicationId}
            allowAll
            placeholder={t("notifications.globalTemplate")}
          />
          <FieldDescription>
            {t("notifications.applicationScopeHint")}
          </FieldDescription>
        </Field>
        <Field>
          <FieldLabel htmlFor="create-template-language">
            {t("notifications.defaultLanguage")}
          </FieldLabel>
          <Select value={defaultLanguage} onValueChange={setDefaultLanguage}>
            <SelectTrigger id="create-template-language">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {SUPPORTED_LANGUAGES.map((language) => (
                  <SelectItem key={language.code} value={language.code}>
                    {language.label}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
      </FieldGroup>
    </ConfirmDialog>
  )
}
