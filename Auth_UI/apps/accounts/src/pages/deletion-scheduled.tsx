import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useLocation } from "react-router-dom"

import { AuthLayout } from "@authsystem/ui/auth-layout"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { formatDate } from "@authsystem/ui/format"

interface LocationState {
  /** From the 202 body of POST /Users/me/deletion. */
  graceEndsAtUtc?: string
}

/**
 * Signed-out landing page right after scheduling one's own account deletion.
 * State-driven on purpose: a refresh loses the deadline, and the page falls
 * back to the sign-in screen (the confirmation email carries the details).
 */
export function DeletionScheduledPage() {
  const { t } = useTranslation()
  const location = useLocation()
  const state = location.state as LocationState | null
  const graceEndsAtUtc = state?.graceEndsAtUtc
  // Freeze the reference instant for this mounted confirmation. Re-renders
  // must not silently change a legal grace-period statement.
  const [mountedAt] = React.useState(() => Date.now())

  if (!graceEndsAtUtc) {
    return <Navigate to="/login" replace />
  }

  const days = Math.max(
    0,
    Math.ceil((new Date(graceEndsAtUtc).getTime() - mountedAt) / 86_400_000)
  )

  return (
    <AuthLayout title={t("accountDeletion.scheduledTitle")}>
      <div className="flex flex-col items-center gap-4">
        <Badge variant="destructive">
          {t("accountDeletion.daysToRecover", { days })}
        </Badge>
        <p className="text-center text-sm text-muted-foreground">
          {t("accountDeletion.scheduledBody", {
            date: formatDate(graceEndsAtUtc),
          })}
        </p>
        <Button asChild variant="outline" className="w-full">
          <Link to="/login">{t("auth.backToSignIn")}</Link>
        </Button>
      </div>
    </AuthLayout>
  )
}
