import { useTranslation } from "react-i18next"

import type { FlowLoginDesign } from "@authsystem/ui/auth-layout"
import { useApplicationTheme, useBranding } from "@authsystem/ui/branding"

import { getReturnToClientId } from "./return-to"
import { type AppBranding, useAppBranding, useAppLoginDesign, useAppTheme } from "./use-app-branding"

export interface FlowBranding {
  /** The requesting application's public branding, or null for the platform's own. */
  appBranding: AppBranding | null
  /** Spread onto `AuthLayout`: the application's header and the trust marker. */
  layout: {
    appName: string | null
    appLogoUrl: string | null
    appLogoUrlDark: string | null
    /** The line under the title naming the application ("Continue to Acme."). */
    continueTo: string | null
    securedBy: string | null
    /**
     * The panel's design on a page of the request's application: undefined without a
     * request, and the page then shows the platform's design.
     */
    loginDesign: FlowLoginDesign | undefined
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
 * The name, the logo and the appearance come from the public-branding endpoint,
 * never from the URL.
 *
 * The same pages show the application's own appearance, while it has one
 * switched on, in place of the platform's: every screen of the flow reads its
 * header here, so none of them can forget it.
 */
export function useFlowBranding(returnTo: string | null): FlowBranding {
  const { t } = useTranslation()
  const clientId = getReturnToClientId(returnTo)
  const appBranding = useAppBranding(clientId)
  const loginDesign = useApplicationLook(clientId)
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
      continueTo: appBranding
        ? t("auth.continueToApp", { name: appBranding.name })
        : null,
      securedBy,
      loginDesign,
    },
  }
}

/**
 * What a page of an application's flow takes from the application besides its
 * header: its appearance, registered for the page, and the design its panel
 * shows, ready for `AuthLayout`'s `loginDesign`. A page with a header of its own
 * (the organization step) takes both here, as `useFlowBranding` does, so no page
 * can show the application's colours around the platform's design, or the
 * reverse.
 *
 * Both belong to the validated pending request only: `clientId` comes from
 * `returnTo` or the request handle, never from anything else in the URL.
 */
export function useApplicationLook(clientId: string | null): FlowLoginDesign | undefined {
  useApplicationTheme(clientId, useAppTheme(clientId))
  const design = useAppLoginDesign(clientId)
  return clientId ? { clientId, design } : undefined
}
