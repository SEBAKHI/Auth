import * as React from "react"
import { useTranslation } from "react-i18next"
import { useBlocker } from "react-router-dom"

import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"

/**
 * Guards a full PAGE against losing unsaved edits, covering both ways out:
 * in-app navigation (router blocker → confirmation dialog) and leaving the
 * site entirely (native beforeunload prompt, which browsers render themselves
 * and cannot be styled).
 *
 * Render the returned `prompt` anywhere in the page. Programmatic navigation
 * after a successful save is unaffected as long as `isDirty` is cleared first.
 *
 * The dialog-scoped equivalent is `useDirtyClose`.
 */
export function useUnsavedChangesPrompt(isDirty: boolean): React.ReactElement | null {
  const { t } = useTranslation()

  const blocker = useBlocker(
    React.useCallback(
      ({ currentLocation, nextLocation }) =>
        isDirty && currentLocation.pathname !== nextLocation.pathname,
      [isDirty]
    )
  )

  // Tab close / reload / external link: only the native prompt can stop these.
  React.useEffect(() => {
    if (!isDirty) return
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      // Legacy browsers require a returnValue to show their own prompt.
      event.returnValue = ""
    }
    window.addEventListener("beforeunload", handler)
    return () => window.removeEventListener("beforeunload", handler)
  }, [isDirty])

  if (blocker.state !== "blocked") return null

  return (
    <ConfirmDialog
      open
      onOpenChange={(open) => {
        if (!open) blocker.reset()
      }}
      title={t("common.discardTitle")}
      description={t("common.discardBody")}
      confirmLabel={t("common.discard")}
      destructive
      onConfirm={() => blocker.proceed()}
    />
  )
}
