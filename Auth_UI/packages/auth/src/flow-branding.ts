import { useTranslation } from "react-i18next"

import { useBranding } from "@authsystem/ui/branding"

import { getReturnToClientId } from "./return-to"
import { type AppBranding, useAppBranding } from "./use-app-branding"

export interface FlowBranding {
  /** The requesting application's public branding, or null for the platform's own. */
  appBranding: AppBranding | null
  /** Spread onto `AuthLayout`: the application's header and the trust marker. */
  layout: {
    appName: string | null
    appLogoUrl: string | null
    appLogoUrlDark: string | null
    securedBy: string | null
  }
}

/**
 * The header of every screen a pending authorize request passes through.
 *
 * The sign-in page used to be the only one that asked: a visitor sent here by an
 * application saw that application on the sign-in page, then the platform on the
 * very next screen, and was left to wonder whether they were still in the right
 * place. Every screen that can carry the request reads its header here, so the
 * next one inherits it instead of re-deriving it.
 *
 * Only the client id is taken from `returnTo`, which must already be validated.
 * The name and the logo come from the public-branding endpoint, never from the URL.
 */
export function useFlowBranding(returnTo: string | null): FlowBranding {
  const { t } = useTranslation()
  const appBranding = useAppBranding(getReturnToClientId(returnTo))
  const { name: platformName, isPending: brandingPending } = useBranding()

  // A trust marker naming the wrong platform is worse than no marker at all:
  // until the branding resolves, `platformName` is the compiled-in default.
  const securedBy = brandingPending
    ? null
    : t("auth.securedBy", { name: platformName })

  return {
    appBranding,
    layout: {
      appName: appBranding?.name ?? null,
      appLogoUrl: appBranding?.logoUrl ?? null,
      appLogoUrlDark: appBranding?.logoUrlDark ?? null,
      securedBy,
    },
  }
}
