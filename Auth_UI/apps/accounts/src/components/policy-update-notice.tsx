import { Info } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"

import { POLICY_VERSION } from "@/pages/privacy/content/types"

const STORAGE_KEY = "privacy.acknowledgedPolicyVersion"

/**
 * The in-app half of the "we notify you of material changes" promise: shown
 * once per browser when the shipped POLICY_VERSION differs from the version
 * last seen here. A first visit adopts the current version silently — new
 * users just accepted the current policy and must not be nagged about it.
 */
export function PolicyUpdateNotice() {
  const { t } = useTranslation()
  const [show, setShow] = React.useState(false)

  React.useEffect(() => {
    try {
      const acknowledged = localStorage.getItem(STORAGE_KEY)
      if (!acknowledged) {
        localStorage.setItem(STORAGE_KEY, POLICY_VERSION)
        return
      }
      if (acknowledged !== POLICY_VERSION) {
        setShow(true)
      }
    } catch {
      /* storage unavailable — the email notice remains the channel */
    }
  }, [])

  const dismiss = React.useCallback(() => {
    try {
      localStorage.setItem(STORAGE_KEY, POLICY_VERSION)
    } catch {
      /* ignore */
    }
    setShow(false)
  }, [])

  if (!show) return null

  return (
    <div className="fixed inset-x-0 bottom-4 z-50 flex justify-center px-4">
      <Alert className="max-w-md shadow-lg">
        <Info />
        <AlertTitle>{t("auth.policyUpdatedNotice")}</AlertTitle>
        <AlertDescription>
          <div className="flex items-center gap-1">
            <Button asChild variant="link" className="px-0">
              <Link to="/privacy" onClick={dismiss}>
                {t("auth.policyUpdatedView")}
              </Link>
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="text-muted-foreground"
              onClick={dismiss}
            >
              {t("auth.policyUpdatedDismiss")}
            </Button>
          </div>
        </AlertDescription>
      </Alert>
    </div>
  )
}
