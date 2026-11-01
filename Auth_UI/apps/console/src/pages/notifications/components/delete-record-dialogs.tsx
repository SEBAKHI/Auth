import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"

/*
 * The delete confirmations of the notification records, one per record type, shared by
 * each record's list row and its own page so the question, the request, the toast and the
 * refetch are written once. Each dialog names the record it deletes. The server applies the
 * rule; the client only decides whether to offer the action (`isSystemGlobalTemplate`,
 * `isGlobalLayout` in `../lib`, and a policy version's `canDelete`).
 */

interface DeleteDialogProps<T> {
  /** The record to delete; null keeps the dialog closed. */
  target: T | null
  onOpenChange: (open: boolean) => void
  /** After the list refetch has been asked for (a record page navigates away here). */
  onDeleted?: () => void
}

export function DeleteTemplateDialog({
  target,
  onOpenChange,
  onDeleted,
}: DeleteDialogProps<{ id?: string; typeName?: string | null }>) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE("/api/v1/notification-templates/{id}", { params: { path: { id } } })),
    onSuccess: () => {
      toast.success(t("notifications.templateDeleted"))
      void queryClient.invalidateQueries({ queryKey: ["notification-templates"] })
      onOpenChange(false)
      onDeleted?.()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={target !== null}
      onOpenChange={(open) => {
        if (!mutation.isPending) onOpenChange(open)
      }}
      title={t("notifications.deleteNamedTitle", { name: target?.typeName ?? "" })}
      description={t("notifications.deleteBody")}
      confirmLabel={t("common.delete")}
      destructive
      loading={mutation.isPending}
      onConfirm={() => {
        if (target?.id) mutation.mutate(target.id)
      }}
    />
  )
}

export function DeleteLayoutDialog({
  target,
  onOpenChange,
  onDeleted,
}: DeleteDialogProps<{ id?: string; name?: string | null }>) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE("/api/v1/notification-layouts/{id}", { params: { path: { id } } })),
    onSuccess: () => {
      toast.success(t("notifications.layoutDeleted"))
      void queryClient.invalidateQueries({ queryKey: ["notification-layouts"] })
      onOpenChange(false)
      onDeleted?.()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={target !== null}
      onOpenChange={(open) => {
        if (!mutation.isPending) onOpenChange(open)
      }}
      title={t("notifications.deleteNamedTitle", { name: target?.name ?? "" })}
      description={t("notifications.deleteLayoutBody")}
      confirmLabel={t("common.delete")}
      destructive
      loading={mutation.isPending}
      onConfirm={() => {
        if (target?.id) mutation.mutate(target.id)
      }}
    />
  )
}

export function DeletePolicyVersionDialog({
  target,
  onOpenChange,
  onDeleted,
}: DeleteDialogProps<{ id?: string; version?: string | null }>) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE("/api/v1/privacy-policy/versions/{id}", { params: { path: { id } } })),
    onSuccess: () => {
      toast.success(t("notifications.policyDeleted"))
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      onOpenChange(false)
      onDeleted?.()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <ConfirmDialog
      open={target !== null}
      onOpenChange={(open) => {
        if (!mutation.isPending) onOpenChange(open)
      }}
      title={t("notifications.policyDeleteTitle", { version: target?.version ?? "" })}
      description={t("notifications.policyDeleteBody")}
      confirmLabel={t("common.delete")}
      destructive
      loading={mutation.isPending}
      onConfirm={() => {
        if (target?.id) mutation.mutate(target.id)
      }}
    />
  )
}
