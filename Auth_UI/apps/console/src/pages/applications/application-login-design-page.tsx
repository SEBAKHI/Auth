import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { Skeleton } from "@authsystem/ui/skeleton"
import { toThemeConfig } from "@authsystem/ui/theme/theme-config"

import { LoginDesignPage } from "@/components/login-designs/login-design-page"
import { PERMISSIONS } from "@/lib/constants"

/**
 * One of an application's sign-in page designs on its own page. Editing needs
 * applications:update; a reader with applications:read sees it read-only. The preview
 * follows the appearance the application's pages use: its own while it is switched on,
 * the platform's otherwise.
 */
export function ApplicationLoginDesignPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const { id: applicationId = "" } = useParams()

  // The application page's own query: already cached when the design was opened from it.
  const application = useQuery({
    queryKey: ["applications", applicationId],
    enabled: applicationId !== "",
    queryFn: () =>
      unwrap(api.GET("/api/v1/Applications/{id}", { params: { path: { id: applicationId } } })),
  })
  const themeEnabled = application.data?.themeEnabled
  const theme = application.data?.theme
  const appearance = React.useMemo(
    () => (themeEnabled && theme ? toThemeConfig(theme) : null),
    [themeEnabled, theme]
  )
  const scope = React.useMemo(() => ({ kind: "application" as const, applicationId }), [applicationId])

  // The preview needs the application's appearance; the editor waits for it, but a failed
  // read of the application must not block the design itself (the platform's look stands in).
  if (application.isLoading) return <Skeleton className="h-[640px] w-full" />

  return (
    <LoginDesignPage
      scope={scope}
      canManage={hasPermission(PERMISSIONS.applications.update)}
      appearance={appearance}
      description={t("loginDesigns.appDescription")}
    />
  )
}
