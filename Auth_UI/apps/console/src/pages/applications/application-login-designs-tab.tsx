import * as React from "react"
import { useTranslation } from "react-i18next"

import type { Schemas } from "@authsystem/api/types"

import { LoginDesignsManager } from "@/components/login-designs/login-designs-manager"

type ApplicationDto = Schemas["ApplicationDto"]

/**
 * The application's "Templates" tab: the designs of the panel half of its sign-in pages,
 * listed like email templates; each opens on its own page. Loaded when the tab is first
 * opened.
 */
export default function ApplicationLoginDesignsTab({
  application,
  canUpdate,
}: {
  application: ApplicationDto
  canUpdate: boolean
}) {
  const { t } = useTranslation()
  const scope = React.useMemo(
    // The tab mounts only once the application is loaded, so its id is known.
    () => ({ kind: "application" as const, applicationId: String(application.id) }),
    [application.id]
  )

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("loginDesigns.appDescription")}</p>
      <LoginDesignsManager
        scope={scope}
        canManage={canUpdate}
        tableId="app-login-designs"
        urlNamespace="designs"
      />
    </div>
  )
}
