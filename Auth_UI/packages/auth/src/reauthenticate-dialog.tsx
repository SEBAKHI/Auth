import * as React from "react"
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

import { useAuth } from "./auth-context"

/**
 * Asks for a fresh sign-in when the API answers `Auth.ReauthenticationRequired`.
 *
 * A change to the second factor needs a sign-in younger than the operator's
 * window (TwoFactor:ReauthenticationMaxAgeMinutes), and a refreshed token does not
 * count: it belongs to the same old session. The API therefore answers 403 rather
 * than 401 — a 401 would make the client refresh and replay, and get the same
 * answer.
 *
 * The way forward is to sign out. The route guard then sends the user to the
 * sign-in page carrying the page they were on (the security tab is in its URL),
 * and the sign-in completion brings them straight back to it — so this dialog
 * never navigates by itself. Cancel leaves everything as it was.
 *
 * The sign-out button is a plain Button, not the dialog's action button, which
 * would close the dialog before the sign-out has finished.
 */
export function ReauthenticateDialog({
  open,
  onOpenChange,
  description,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Why a fresh sign-in is needed; the two-factor change's reason by default. */
  description?: string
}) {
  const { t } = useTranslation()
  const { logout } = useAuth()
  const [signingOut, setSigningOut] = React.useState(false)
  // Set synchronously in the handler: a double click lands before the
  // re-render that disables the button.
  const signingOutRef = React.useRef(false)

  const signInAgain = async () => {
    if (signingOutRef.current) return
    signingOutRef.current = true
    setSigningOut(true)
    try {
      await logout()
    } finally {
      signingOutRef.current = false
      setSigningOut(false)
    }
  }

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        if (!signingOutRef.current) onOpenChange(next)
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t("auth.reauthenticateTitle")}</AlertDialogTitle>
          <AlertDialogDescription>
            {description ?? t("auth.reauthenticateBody")}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={signingOut}>
            {t("common.cancel")}
          </AlertDialogCancel>
          <Button onClick={() => void signInAgain()} disabled={signingOut}>
            {signingOut ? <Spinner data-icon="inline-start" /> : null}
            {t("auth.reauthenticateAction")}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
