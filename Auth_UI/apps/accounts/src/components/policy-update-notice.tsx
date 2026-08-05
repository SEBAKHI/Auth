import { useQuery } from "@tanstack/react-query"
import { Info } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { privacyPolicyUrl } from "@authsystem/api/env"
import { unwrap } from "@authsystem/api/helpers"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"

const STORAGE_KEY = "privacy.acknowledgedPolicyVersion"

/**
 * The in-app half of the "we notify you of material changes" promise: shown
 * once per browser when the PUBLISHED policy version differs from the one last
 * seen here. A first visit adopts the current version silently — new users just
 * accepted the current policy and must not be nagged about it.
 *
 * The version comes from the server, not from a compiled-in constant. Reading a
 * build-time value meant this described the bundle rather than the document
 * people are actually served: a policy published without a frontend deploy went
 * unannounced, and a frontend deploy without a publish announced nothing.
 */
export function PolicyUpdateNotice() {
  const { t, i18n } = useTranslation()
  const [show, setShow] = React.useState(false)

  const { data } = useQuery({
    queryKey: ["privacy-policy-version"],
    queryFn: () =>
      unwrap(api.GET("/api/v1/privacy-policy/published", { params: { query: {} } })),
    staleTime: 5 * 60 * 1000,
  })

  const publishedVersion = data?.version ?? null

  React.useEffect(() => {
    // Nothing is claimed until the published version is known: announcing a
    // change against an unknown version is how the old constant misfired.
    if (!publishedVersion) return

    try {
      const acknowledged = localStorage.getItem(STORAGE_KEY)
      if (!acknowledged) {
        localStorage.setItem(STORAGE_KEY, publishedVersion)
        return
      }
      if (acknowledged !== publishedVersion) {
        setShow(true)
      }
    } catch {
      /* storage unavailable — the email notice remains the channel */
    }
  }, [publishedVersion])

  const dismiss = React.useCallback(() => {
    try {
      if (publishedVersion) localStorage.setItem(STORAGE_KEY, publishedVersion)
    } catch {
      /* ignore */
    }
    setShow(false)
  }, [publishedVersion])

  if (!show) return null

  return (
    <div className="fixed inset-x-0 bottom-4 z-50 flex justify-center px-4">
      <Alert className="max-w-md shadow-lg">
        <Info />
        <AlertTitle>{t("auth.policyUpdatedNotice")}</AlertTitle>
        <AlertDescription>
          <div className="flex items-center gap-1">
            {/* A document served by the API, not a route in this app. */}
            <Button asChild variant="link" className="px-0">
              <a href={privacyPolicyUrl(i18n.language)} onClick={dismiss}>
                {t("auth.policyUpdatedView")}
              </a>
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
