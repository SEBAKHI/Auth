import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Plus, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Button } from "@astoom/ui/button"
import { Checkbox } from "@astoom/ui/checkbox"
import { Input } from "@astoom/ui/input"
import { Label } from "@astoom/ui/label"
import type { NotificationTemplateDetailDto, TemplateVariable } from "../lib"
import { parseVariables } from "../lib"

/**
 * Edits the notification TYPE's variable catalog (shared by every template of
 * that type). The catalog is the contract with the sending code — a template may
 * only reference variables listed here — so this dialog is where an admin adds a
 * custom variable, documents it, and marks it required/optional. Sample data for
 * previews is kept in sync (added variables get an empty sample entry).
 */
export function ManageVariablesDialog({
  open,
  onOpenChange,
  template,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  template: NotificationTemplateDetailDto
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [rows, setRows] = React.useState<TemplateVariable[]>([])

  React.useEffect(() => {
    if (open) {
      setRows(parseVariables(template.typeVariablesJson))
    }
  }, [open, template.typeVariablesJson])

  const patch = (index: number, change: Partial<TemplateVariable>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...change } : row)))

  const saveMutation = useMutation({
    mutationFn: () => {
      // Keep sample data aligned: preserve existing values, seed new keys empty.
      let sample: Record<string, unknown> = {}
      try {
        const parsed: unknown = JSON.parse(template.typeSampleDataJson ?? "{}")
        if (parsed && typeof parsed === "object") sample = parsed as Record<string, unknown>
      } catch {
        sample = {}
      }
      const cleaned = rows
        .map((r) => ({ ...r, name: r.name.trim() }))
        .filter((r) => r.name)
      const nextSample: Record<string, unknown> = {}
      for (const row of cleaned) {
        nextSample[row.name] = sample[row.name] ?? row.example ?? ""
      }

      return unwrap(
        api.PUT("/api/v1/notification-types/{id}", {
          params: { path: { id: template.notificationTypeId! } },
          body: {
            name: template.typeName ?? "",
            description: null,
            variablesJson: JSON.stringify(cleaned),
            sampleDataJson: JSON.stringify(nextSample),
          },
        })
      )
    },
    onSuccess: () => {
      toast.success(t("notifications.variablesSaved"))
      void queryClient.invalidateQueries({ queryKey: ["notification-template", template.id] })
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("notifications.manageVariablesTitle")}
      description={t("notifications.manageVariablesHint")}
      confirmLabel={t("common.save")}
      loading={saveMutation.isPending}
      onConfirm={() => saveMutation.mutate()}
    >
      <div className="space-y-4">
        <div className="max-h-[45vh] space-y-3 overflow-y-auto pe-1">
          {rows.map((row, index) => (
            <div key={index} className="space-y-2 rounded-md border p-3">
              <div className="grid gap-2 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label className="text-xs">{t("notifications.variableName")}</Label>
                  <Input
                    dir="ltr"
                    value={row.name}
                    onChange={(e) => patch(index, { name: e.target.value })}
                    placeholder="CompanyPhone"
                  />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">{t("notifications.variableExample")}</Label>
                  <Input
                    dir="auto"
                    value={row.example ?? ""}
                    onChange={(e) => patch(index, { example: e.target.value })}
                  />
                </div>
              </div>
              <div className="space-y-1">
                <Label className="text-xs">{t("notifications.variableDescription")}</Label>
                <Input
                  dir="auto"
                  value={row.description ?? ""}
                  onChange={(e) => patch(index, { description: e.target.value })}
                />
              </div>
              <div className="flex items-center justify-between">
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={row.required ?? false}
                    onCheckedChange={(checked) => patch(index, { required: checked === true })}
                  />
                  {t("notifications.variableRequiredLabel")}
                </label>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("notifications.removeVariable")}
                  onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
                >
                  <Trash2 />
                </Button>
              </div>
            </div>
          ))}
        </div>

        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() =>
            setRows((current) => [...current, { name: "", description: "", example: "", required: false }])
          }
        >
          <Plus data-icon="inline-start" />
          {t("notifications.addVariable")}
        </Button>

        <p className="rounded-md bg-muted/50 p-3 text-xs text-muted-foreground">
          {t("notifications.variablesContextHint")}
        </p>
        <p className="text-xs text-muted-foreground">{t("notifications.customVariableWarning")}</p>
      </div>
    </ConfirmDialog>
  )
}
