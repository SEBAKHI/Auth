import { useTranslation } from "react-i18next"

import type { FlowLoginDesign } from "@authsystem/ui/auth-layout"
import { useApplicationTheme, useBranding } from "@authsystem/ui/branding"

import { getReturnToClientId } from "./return-to"
import {
  type AppBranding,
  useAppBranding,
  useAppKnown,
  useAppLoginDesign,
  useAppTheme,
} from "./use-app-branding"

/**
 * What names the application on a page:
 * - `returnTo`: a screen of a pending authorize request, which must already be
 *   validated. Its client id is the only thing read from it.
 * - `clientId`: a page that names an application in its own address, with no
 *   request to resume: the sign-out confirmation, the signed-out page and the
 *   expired request. The address may name any client id; only the server's
 *   answer for it is ever shown.
 */
export type NamedApplication =
  | { returnTo: string | null }
  | { clientId: string | null }

export interface FlowBranding {
  /** The named application's public branding, or null for the platform's own. */
  appBranding: AppBranding | null
  /** Spread onto `AuthLayout`: the application's header and the trust marker. */
  layout: {
    appName: string | null
    appLogoUrl: string | null
    appLogoUrlDark: string | null
    /** The height its logo is drawn at; null while none is chosen (the default). */
    appLogoHeightPx: number | null
    /**
     * The line under the title naming the application ("Continue to Acme."),
     * on a screen of a pending request only: the visitor goes on to the
     * application from there, and from no other page.
     */
    continueTo: string | null
    securedBy: string | null
    /**
     * The panel's design on a page of the named application: undefined when
     * the page names none, or one the server does not know or has switched
     * off, and the page then shows the platform's design.
     */
    loginDesign: FlowLoginDesign | undefined
  }
}

/**
 * The header of every page that names an application: the screens a pending
 * authorize request passes through, the organization step, the sign-out pages
 * and the expired request.
 *
 * The sign-in page used to be the only one that asked: a visitor sent here by an
 * application saw that application on the sign-in page, then the platform on the
 * very next screen, and was left to wonder whether they were still in the right
 * place. Every such page reads its header here, so none of them re-derives it
 * and none of them can forget a part of it.
 *
 * Its parts, all from the public-branding endpoint and never from the URL: the
 * name and the logos, the application's own appearance while it has one switched
 * on (in place of the platform's), the design its panel shows, and the trust
 * marker naming the platform. An application the server does not know, or has
 * switched off, names nothing: the page is the platform's own, in its appearance
 * and its design.
 *
 * No page calls the branding hooks it is built from; a test reads the sources
 * to hold every page to this one.
 */
export function useFlowBranding(named: NamedApplication): FlowBranding {
  const { t } = useTranslation()
  const ofRequest = !("clientId" in named)
  const clientId = "clientId" in named ? named.clientId || null : getReturnToClientId(named.returnTo)
  const appBranding = useAppBranding(clientId)
  const known = useAppKnown(clientId)
  useApplicationTheme(clientId, useAppTheme(clientId))
  const appDesign = useAppLoginDesign(clientId)
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
      appLogoHeightPx: appBranding?.logoHeightPx ?? null,
      continueTo:
        ofRequest && appBranding
          ? t("auth.continueToApp", { name: appBranding.name })
          : null,
      securedBy,
      loginDesign:
        clientId && known !== false ? { clientId, design: appDesign } : undefined,
    },
  }
}
