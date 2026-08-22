import * as React from "react"
import { useTranslation } from "react-i18next"
import { useBlocker } from "react-router-dom"

import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"

/**
 * Guards a full PAGE against losing unsaved edits, covering both ways out:
 * in-app navigation (router blocker → confirmation dialog) and leaving the
 * site entirely (native beforeunload prompt, which browsers render themselves
 * and cannot be styled).
 *
 * Render the returned prompt anywhere in the page. A navigation requested
 * while saving resumes automatically only when the save also clears dirty
 * state; a failed save keeps the request blocked for an explicit decision.
 *
 * The dialog-scoped equivalent is `useDirtyClose`.
 */
export function useUnsavedChangesPrompt({
  isDirty,
  isSaving = false,
}: {
  isDirty: boolean
  isSaving?: boolean
}): React.ReactElement | null {
  const { t } = useTranslation()
  const shouldBlock = isDirty || isSaving

  const blocker = useBlocker(
    React.useCallback(
      ({ currentLocation, nextLocation }) =>
        shouldBlock && currentLocation.pathname !== nextLocation.pathname,
      [shouldBlock]
    )
  )

  React.useEffect(() => {
    if (blocker.state === "blocked" && !shouldBlock) {
      blocker.proceed()
    }
  }, [blocker, shouldBlock])

  // Tab close / reload / external link: only the native prompt can stop these.
  React.useEffect(() => {
    if (!shouldBlock) return
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      // Legacy browsers require a returnValue to show their own prompt.
      event.returnValue = ""
    }
    window.addEventListener("beforeunload", handler)
    return () => window.removeEventListener("beforeunload", handler)
  }, [shouldBlock])

  if (blocker.state !== "blocked") return null

  return (
    <ConfirmDialog
      open
      onOpenChange={(open) => {
        if (!open) blocker.reset()
      }}
      title={
        isSaving
          ? t("common.saveInProgressTitle")
          : t("common.discardTitle")
      }
      description={
        isSaving
          ? t("common.saveInProgressBody")
          : t("common.discardBody")
      }
      confirmLabel={t("common.discard")}
      destructive
      confirmDisabled={isSaving}
      onConfirm={() => blocker.proceed()}
    />
  )
}
