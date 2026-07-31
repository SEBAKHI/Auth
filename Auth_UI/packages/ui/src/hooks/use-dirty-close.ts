import * as React from "react"
import { useTranslation } from "react-i18next"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"

/**
 * Guards a dialog's close affordances against losing unsaved edits.
 *
 * Pass the form's dirty flag and the dialog's `onOpenChange`. Use the returned
 * `requestOpenChange` as the dialog's `onOpenChange` (and on the Cancel button);
 * a close while dirty is intercepted by a "Discard changes?" confirmation, while
 * a clean close passes straight through. Render `discardDialog` inside the dialog.
 *
 * Programmatic closes after a successful save should call the original
 * `onOpenChange(false)` so they bypass the guard.
 */
export function useDirtyClose({
  isDirty,
  onOpenChange,
}: {
  isDirty: boolean
  onOpenChange: (open: boolean) => void
}): {
  requestOpenChange: (open: boolean) => void
  discardDialog: React.ReactElement
} {
  const { t } = useTranslation()
  const [confirmOpen, setConfirmOpen] = React.useState(false)

  const requestOpenChange = React.useCallback(
    (open: boolean) => {
      if (!open && isDirty) {
        setConfirmOpen(true)
        return
      }
      onOpenChange(open)
    },
    [isDirty, onOpenChange]
  )

  const discardDialog = React.createElement(ConfirmDialog, {
    open: confirmOpen,
    onOpenChange: setConfirmOpen,
    title: t("common.discardTitle"),
    description: t("common.discardBody"),
    confirmLabel: t("common.discard"),
    destructive: true,
    onConfirm: () => {
      setConfirmOpen(false)
      onOpenChange(false)
    },
  })

  return { requestOpenChange, discardDialog }
}
