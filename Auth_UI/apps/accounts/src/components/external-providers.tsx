import { useTranslation } from "react-i18next"

import { Separator } from "@authsystem/ui/separator"

import { AppleSignIn } from "@/components/apple-sign-in"
import { GoogleSignIn } from "@/components/google-sign-in"
import { useExternalProviders } from "@/components/use-external-providers"

/**
 * External sign-in section under the credentials form: renders the
 * "or continue with" divider once, followed by every enabled provider button.
 * Renders nothing when no provider is usable.
 */
export function ExternalProviders() {
  const { t } = useTranslation()
  const { googleEnabled, appleEnabled } = useExternalProviders()

  if (!googleEnabled && !appleEnabled) return null

  return (
    <div className="mt-6 flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Separator className="flex-1" />
        <span className="text-xs text-muted-foreground">
          {t("auth.orContinueWith")}
        </span>
        <Separator className="flex-1" />
      </div>
      <GoogleSignIn />
      <AppleSignIn />
    </div>
  )
}
