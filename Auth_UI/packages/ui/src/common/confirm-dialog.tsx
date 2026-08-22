import type * as React from "react"
import { useTranslation } from "react-i18next"

import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@authsystem/ui/alert-dialog"
import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"

/**
 * Controlled confirmation dialog for destructive or sensitive actions.
 * The confirm button uses a plain Button (not AlertDialogAction) so the dialog
 * stays open during async work and is closed by the caller on success.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive = false,
  loading = false,
  confirmDisabled = false,
  onConfirm,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  confirmLabel?: string
  destructive?: boolean
  loading?: boolean
  /** Keeps the confirm button disabled, e.g. until a type-to-confirm check passes. */
  confirmDisabled?: boolean
  onConfirm: () => void
  children?: React.ReactNode
}) {
  const { t } = useTranslation()

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          {description ? (
            <AlertDialogDescription>{description}</AlertDialogDescription>
          ) : null}
        </AlertDialogHeader>
        {children ? (
          <div className="flex flex-col gap-3">{children}</div>
        ) : null}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={loading}>
            {t("common.cancel")}
          </AlertDialogCancel>
          <Button
            variant={destructive ? "destructive" : "default"}
            onClick={onConfirm}
            disabled={loading || confirmDisabled}
          >
            {loading ? <Spinner data-icon="inline-start" /> : null}
            {confirmLabel ?? t("common.confirm")}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
