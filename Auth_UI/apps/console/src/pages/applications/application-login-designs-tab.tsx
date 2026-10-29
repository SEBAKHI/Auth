import * as React from "react"
import { useTranslation } from "react-i18next"

import type { Schemas } from "@authsystem/api/types"
import { toThemeConfig } from "@authsystem/ui/theme/theme-config"

import { LoginDesignsManager } from "@/components/login-designs/login-designs-manager"

type ApplicationDto = Schemas["ApplicationDto"]

/**
 * The application's "Templates" tab: the designs of the panel half of its sign-in pages,
 * managed like notification templates. Loaded when the tab is first opened.
 *
 * The preview follows the appearance the application's pages use: its own while it is
 * switched on, the platform's otherwise.
 */
export default function ApplicationLoginDesignsTab({
  application,
  canUpdate,
}: {
  application: ApplicationDto
  canUpdate: boolean
}) {
  const { t } = useTranslation()
  const appearance = React.useMemo(
    () => (application.themeEnabled && application.theme ? toThemeConfig(application.theme) : null),
    [application.themeEnabled, application.theme]
  )
  const scope = React.useMemo(
    // The tab mounts only once the application is loaded, so its id is known.
    () => ({ kind: "application" as const, applicationId: String(application.id) }),
    [application.id]
  )

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("loginDesigns.appDescription")}</p>
      <LoginDesignsManager scope={scope} canManage={canUpdate} appearance={appearance} />
    </div>
  )
}
