import { useTranslation } from "react-i18next"

import { Separator } from "@authsystem/ui/separator"

import { AppleSignIn } from "./apple-sign-in"
import { GoogleSignIn } from "./google-sign-in"
import { useExternalProviders } from "./use-external-providers"

interface ExternalProvidersProps {
  /**
   * Route to send a pending-deletion account to, forwarded to every provider
   * button. Omit it in an app that has no recovery screen — see GoogleSignIn.
   */
  recoveryPath?: string
}

/**
 * External sign-in section under the credentials form: renders the "or"
 * divider once, followed by every enabled provider button. Renders nothing
 * when no provider is usable.
 *
 * Lives in @authsystem/auth rather than in one app because both the accounts
 * app and the console mount the same shared LoginPage and fill its `providers`
 * slot with this; a provider added here reaches both at once.
 */
export function ExternalProviders({ recoveryPath }: ExternalProvidersProps = {}) {
  const { t } = useTranslation()
  const { googleEnabled, appleEnabled } = useExternalProviders()

  if (!googleEnabled && !appleEnabled) return null

  return (
    <div className="mt-6 flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Separator className="flex-1" />
        <span className="text-xs text-muted-foreground">
          {t("auth.or")}
        </span>
        <Separator className="flex-1" />
      </div>
      <GoogleSignIn recoveryPath={recoveryPath} />
      <AppleSignIn recoveryPath={recoveryPath} />
    </div>
  )
}
