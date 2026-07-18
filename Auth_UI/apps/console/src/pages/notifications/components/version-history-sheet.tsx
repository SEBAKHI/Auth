import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { toNumber, unwrap } from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@astoom/ui/sheet"
import { formatDateTime } from "@astoom/ui/format"
import type { NotificationTemplateDetailDto } from "../lib"

/**
 * Version history with one-click rollback (the published pointer moves and every
 * translation of the target version returns together) and restore-as-draft.
 */
export function VersionHistorySheet({
  open,
  onOpenChange,
  template,
  canPublish,
  canManage,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  template: NotificationTemplateDetailDto
  canPublish: boolean
  canManage: boolean
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [rollbackTarget, setRollbackTarget] = React.useState<
    { id: string; versionNumber: number } | undefined
  >()

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["notification-template", template.id] })

  const rollbackMutation = useMutation({
    mutationFn: (targetVersionId: string) =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/rollback", {
          params: { path: { id: template.id! } },
          body: { targetVersionId },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.rolledBack"))
      setRollbackTarget(undefined)
      void invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const restoreMutation = useMutation({
    mutationFn: (versionId: string) =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/versions/{versionId}/restore-draft", {
          params: { path: { id: template.id!, versionId } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.restoredAsDraft"))
      onOpenChange(false)
      void invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>{t("notifications.versionHistory")}</SheetTitle>
          <SheetDescription>{t("notifications.versionHistoryHint")}</SheetDescription>
        </SheetHeader>

        <div className="space-y-3 px-4 pb-6">
          {(template.versions ?? []).map((version) => (
            <div key={version.id} className="space-y-2 rounded-md border p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">
                  {t("notifications.versionLabel", { version: version.versionNumber })}
                </span>
                {version.isPublished ? (
                  <Badge>{t("notifications.published")}</Badge>
                ) : null}
                {version.isDraft ? (
                  <Badge variant="secondary">{t("notifications.draft")}</Badge>
                ) : null}
                <span className="ms-auto text-xs text-muted-foreground">
                  {formatDateTime(version.createdAt)}
                </span>
              </div>
              <p className="text-sm text-muted-foreground">
                {version.changeNote || t("notifications.noChangeNote")}
              </p>
              <p className="text-xs text-muted-foreground">
                {t("notifications.translationCount", { count: version.translationCount ?? 0 })}
              </p>
              {!version.isDraft ? (
                <div className="flex gap-2">
                  {canPublish && !version.isPublished ? (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        version.id &&
                        setRollbackTarget({
                          id: version.id,
                          versionNumber: toNumber(version.versionNumber),
                        })
                      }
                    >
                      {t("notifications.rollback")}
                    </Button>
                  ) : null}
                  {canManage && !template.draftVersionId ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={restoreMutation.isPending}
                      onClick={() => version.id && restoreMutation.mutate(version.id)}
                    >
                      {t("notifications.restoreAsDraft")}
                    </Button>
                  ) : null}
                </div>
              ) : null}
            </div>
          ))}
        </div>
      </SheetContent>

      <ConfirmDialog
        open={Boolean(rollbackTarget)}
        onOpenChange={(nextOpen) => !nextOpen && setRollbackTarget(undefined)}
        title={t("notifications.rollbackTitle", {
          version: rollbackTarget?.versionNumber ?? 0,
        })}
        description={t("notifications.rollbackBody")}
        confirmLabel={t("notifications.rollback")}
        loading={rollbackMutation.isPending}
        onConfirm={() => rollbackTarget && rollbackMutation.mutate(rollbackTarget.id)}
      />
    </Sheet>
  )
}
