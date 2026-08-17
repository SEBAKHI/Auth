import { useTranslation } from "react-i18next"

import { Separator } from "@authsystem/ui/separator"

import { AppleSignIn } from "./apple-sign-in"
import { GoogleSignIn } from "./google-sign-in"
import { useExternalProviders } from "./use-external-providers"

interface ExternalProvidersProps {
  /**
   * Where to send a pending-deletion account, forwarded to every provider
   * button. A route in this app, or an absolute URL when the recovery screen
   * lives on another origin — see GoogleSignIn.
   */
  recoveryPath?: string
  /**
   * Capture mode, forwarded to every provider button: hand the credential to
   * the caller instead of signing in. The recovery screen uses this to obtain
   * the credential the emailed link could not carry.
   */
  onCredential?: (credential: {
    provider: string
    idToken: string
    nonce?: string
  }) => void
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
export function ExternalProviders({
  recoveryPath,
  onCredential,
}: ExternalProvidersProps = {}) {
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
      <GoogleSignIn recoveryPath={recoveryPath} onCredential={onCredential} />
      <AppleSignIn recoveryPath={recoveryPath} onCredential={onCredential} />
    </div>
  )
}
