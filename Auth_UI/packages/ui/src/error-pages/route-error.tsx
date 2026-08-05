import * as React from "react"
import { useTranslation } from "react-i18next"
import { useRouteError } from "react-router-dom"

import { Button } from "@authsystem/ui/button"
import {
  canRecoverFromChunkLoadError,
  isChunkLoadError,
  recoverFromChunkLoadError,
} from "@authsystem/ui/common/chunk-recovery"
import { RouteFallback } from "@authsystem/ui/lazy-route"

/**
 * Root `errorElement`. Without one, a route that throws renders nothing at all —
 * which is what a deploy used to look like: the tab still runs the previous
 * build, asks for a chunk that no longer exists, and goes blank with no way out.
 *
 * A missing chunk is recoverable, so it is recovered rather than reported: one
 * reload fetches the current index document and its new chunk names. Everything
 * else is a real fault and is shown, with a manual reload as the only offer —
 * "go home" would route through the same broken router.
 */
export function RouteErrorBoundary() {
  const { t } = useTranslation()
  const error = useRouteError()

  // Decided with a pure read so the render stays free of the side effect it is
  // describing: the spinner shows only while a reload is genuinely coming, and
  // a build that already failed to recover falls straight through to the fault.
  const recovering = isChunkLoadError(error) && canRecoverFromChunkLoadError()

  React.useEffect(() => {
    if (recovering) recoverFromChunkLoadError()
  }, [recovering])

  if (recovering) return <RouteFallback />

  return (
    <div className="flex min-h-svh flex-col items-center justify-center gap-4 p-6 text-center">
      <div className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">
          {t("errors.unexpectedTitle")}
        </h1>
        <p className="max-w-sm text-sm text-muted-foreground">
          {t("errors.generic")}
        </p>
      </div>
      <Button onClick={() => window.location.reload()}>
        {t("errors.reload")}
      </Button>
    </div>
  )
}
