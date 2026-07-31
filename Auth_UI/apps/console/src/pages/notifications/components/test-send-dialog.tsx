import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"

/**
 * Sends a real test message rendered from the current draft (or the published
 * version) with the type's sample data. Honors Email.Enabled: in development
 * the API logs the message instead of delivering it.
 */
export function TestSendDialog({
  open,
  onOpenChange,
  templateId,
  defaultLanguage,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  templateId: string
  defaultLanguage: string
}) {
  const { t } = useTranslation()
  const [recipientEmail, setRecipientEmail] = React.useState("")
  const [languageCode, setLanguageCode] = React.useState(defaultLanguage)

  const sendMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/test-send", {
          params: { path: { id: templateId } },
          body: { languageCode, recipientEmail },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.testSent"))
      onOpenChange(false)
      setRecipientEmail("")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("notifications.testSendTitle")}
      description={t("notifications.testSendHint")}
      confirmLabel={t("notifications.testSend")}
      loading={sendMutation.isPending}
      onConfirm={() => recipientEmail && sendMutation.mutate()}
    >
      <FieldGroup>
        <Field>
          <FieldLabel htmlFor="test-send-email">
            {t("notifications.recipientEmail")}
          </FieldLabel>
          <Input
            id="test-send-email"
            type="email"
            dir="ltr"
            placeholder="name@example.com"
            value={recipientEmail}
            onChange={(e) => setRecipientEmail(e.target.value)}
          />
        </Field>
        <Field>
          <FieldLabel htmlFor="test-send-language">
            {t("notifications.language")}
          </FieldLabel>
          <Select value={languageCode} onValueChange={setLanguageCode}>
            <SelectTrigger id="test-send-language">
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
