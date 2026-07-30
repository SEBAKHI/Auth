import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Input } from "@astoom/ui/input"
import { Label } from "@astoom/ui/label"
import {
  Select,
  SelectContent,
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
      <div className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="test-send-email">{t("notifications.recipientEmail")}</Label>
          <Input
            id="test-send-email"
            type="email"
            dir="ltr"
            placeholder="name@example.com"
            value={recipientEmail}
            onChange={(e) => setRecipientEmail(e.target.value)}
          />
        </div>
        <div className="space-y-2">
          <Label>{t("notifications.language")}</Label>
          <Select value={languageCode} onValueChange={setLanguageCode}>
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
