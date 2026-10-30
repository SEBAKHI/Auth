import * as React from "react"
import { ShieldCheck } from "lucide-react"

import { LanguageToggle } from "@authsystem/ui/common/language-toggle"
import { ThemeToggle } from "@authsystem/ui/common/theme-toggle"
import { Card, CardContent } from "@authsystem/ui/card"
import { BrandingLogo, useBranding, type LoginDesignRef } from "@authsystem/ui/branding"
import type { DesignFrameState } from "@authsystem/ui/login-design/design-frame"
import LoginDesignPanel from "@authsystem/ui/login-design/login-design-panel"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"
import { cn } from "@authsystem/ui/utils"

// Imported with the page, not as a chunk of its own: it is 2 KB, and a lazy component put
// a round trip in front of the design on a first visit and, on every visit, React's hold
// of up to 300 ms before revealing what a Suspense boundary was waiting for (measured on
// a deployed instance, 2026-10-30). It is still mounted only where the panel shows and a design
// is named, so nothing of a design is fetched elsewhere.

/** The panel is shown from the `lg` breakpoint (1024px) up. */
const PANEL_QUERY = "(min-width: 1024px)"

function subscribePanel(onStoreChange: () => void) {
  const query = window.matchMedia(PANEL_QUERY)
  query.addEventListener("change", onStoreChange)
  return () => query.removeEventListener("change", onStoreChange)
}

/** Whether the panel is on screen: nothing of a design is fetched where it is not. */
function usePanelShown() {
  return React.useSyncExternalStore(
    subscribePanel,
    () => window.matchMedia(PANEL_QUERY).matches,
    () => false
  )
}

/**
 * The design on a page of an application's flow: the application (its client id) and the
 * design its public branding named — its own active design, else the platform's; null for
 * the plain panel; undefined while the branding is not known, so the panel shows no guess.
 */
export type FlowLoginDesign = { clientId: string; design: LoginDesignRef | null | undefined }

/**
 * The page shared by all unauthenticated auth screens, in two halves.
 *
 * The half on the inline-start side is the platform's: its mark in the corner,
 * the screen's title and form, and the trust marker. The other half is the
 * panel, shown from the `lg` breakpoint up; it is a plain surface until an
 * application's design fills it.
 *
 * Only one logo is large. Behind a pending authorize request the application
 * is where the visitor is going, so its logo sits above the title with its name
 * spelled out under it, and the platform's mark stays small in the corner; the
 * trust marker below names the platform. The panel never carries the visitor's
 * bearings: under `lg` it is gone, and the form half has to say on its own
 * which application this is.
 */
export function AuthLayout({
  title,
  subtitle,
  children,
  footer,
  pageFooter,
  appName,
  appLogoUrl,
  appLogoUrlDark,
  continueTo,
  showPlatformLogo,
  securedBy,
  loginDesign,
}: {
  title: string
  subtitle?: string
  children: React.ReactNode
  footer?: React.ReactNode
  /**
   * Page-level footer at the bottom of the form half, visually detached from
   * the auth card — for ambient links (privacy policy) that must not compete
   * with the card's own actions.
   */
  pageFooter?: React.ReactNode
  /**
   * Display name of the application behind a pending authorize request.
   * When set, the page shows that application's logo above the title and the
   * persistent `securedBy` trust marker. Must come from the public-branding
   * endpoint — never from URL parameters.
   */
  appName?: string | null
  /** Logo of that application. */
  appLogoUrl?: string | null
  /**
   * Its dark-mode logo, shown while the dark theme is active; the light one
   * stands in for it when absent (and the other way round), as for the
   * platform's own logos.
   */
  appLogoUrlDark?: string | null
  /** The line under the title naming the application, e.g. "Continue to Acme." */
  continueTo?: string | null
  /**
   * False when the application's design hides the platform's mark: the
   * application's logo then takes the corner, and the trust marker still names
   * the platform. Ignored without an application, on the platform's own pages.
   * Left out, the design's own choice decides (shown when there is no design).
   */
  showPlatformLogo?: boolean
  /** Trust marker under the card, e.g. "Secured by Acme". */
  securedBy?: React.ReactNode
  /**
   * The design of a page of an application's flow (`useFlowBranding`). Left out, the
   * page is the platform's own and shows the platform's design.
   */
  loginDesign?: FlowLoginDesign
}) {
  const resolvedTheme = useResolvedTheme()
  const { loginDesign: platformDesign } = useBranding()
  // The application's flow names its design (its own, else the platform's, decided by the
  // server); the platform's own pages show the platform's.
  const design = loginDesign ? loginDesign.design : platformDesign
  const designClientId = loginDesign?.clientId ?? null
  const panelShown = usePanelShown()
  const panelKey = design && panelShown ? `${designClientId ?? ""}|${design.id}|${design.contentHash}` : null
  // The state belongs to one design: another design starts again from "pending".
  const [panel, setPanel] = React.useState<{ key: string | null; state: DesignFrameState }>({
    key: null,
    state: "pending",
  })
  const panelState = panel.key === panelKey ? panel.state : "pending"
  const keepsPlatformLogo = showPlatformLogo ?? design?.showPlatformLogo ?? true
  // A logo that fails to load is dropped, not drawn as a broken image beside
  // the application name it would repeat. Kept by address, so another logo
  // (the other mode, a new upload) is tried afresh.
  const [failedLogo, setFailedLogo] = React.useState<string | null>(null)
  const pickedLogo = appName
    ? pickLogo(appLogoUrl ?? null, appLogoUrlDark ?? null, resolvedTheme)
    : null
  const appLogo = pickedLogo !== failedLogo ? pickedLogo : null
  const dropLogo = () => setFailedLogo(pickedLogo)
  const appTakesCorner = Boolean(appName) && !keepsPlatformLogo
  // The application is named nowhere else on a page that has no "Continue to" line
  // (logout, signed out): without a usable logo there, its name is written instead.
  const appNamedAsText = Boolean(appName) && !appTakesCorner && !appLogo && !continueTo

  let corner: React.ReactNode
  if (appTakesCorner) {
    corner = appLogo ? (
      <img
        src={appLogo}
        alt={appName!}
        onError={dropLogo}
        className="h-10 w-auto max-w-40 object-contain"
      />
    ) : (
      <CornerName name={appName!} />
    )
  } else {
    // Small while the application's logo is the large one, as on every
    // screen of the flow; at its full corner size on the platform's own pages.
    const compact = Boolean(appName)
    corner = (
      <BrandingLogo
        className={cn(compact ? "h-7" : "h-10", "w-auto max-w-40 object-contain")}
        fallback={(name) => (
          <span className="flex min-w-0 items-center gap-2">
            <span
              className={cn(
                "flex shrink-0 items-center justify-center rounded-lg bg-primary text-primary-foreground",
                compact ? "size-7" : "size-10"
              )}
            >
              <ShieldCheck className={compact ? "size-4" : "size-5"} />
            </span>
            <CornerName name={name} />
          </span>
        )}
      />
    )
  }

  return (
    <div className="grid min-h-svh lg:grid-cols-2">
      <div className="flex min-w-0 flex-col gap-6 p-4 md:p-6">
        <header className="flex items-center justify-between gap-4">
          <div className="flex min-w-0 items-center">{corner}</div>
          <div className="flex shrink-0 items-center gap-1">
            <LanguageToggle />
            <ThemeToggle />
          </div>
        </header>

        <main className="flex flex-1 flex-col items-center justify-center">
          <div className="w-full max-w-sm">
            <div className="mb-6 flex flex-col items-center gap-2 text-center">
              {appLogo && !appTakesCorner ? (
                <img
                  src={appLogo}
                  alt={appName!}
                  onError={dropLogo}
                  className="mb-2 h-12 w-auto max-w-60 object-contain"
                />
              ) : null}
              {appNamedAsText ? (
                // An application's name may run to 200 characters with no space.
                <p className="mb-2 max-w-full text-sm font-semibold wrap-anywhere">{appName}</p>
              ) : null}

              <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
              {appName && continueTo ? (
                // An application's name may run to 200 characters with no space.
                <p className="max-w-full text-sm wrap-anywhere text-muted-foreground">
                  {continueTo}
                </p>
              ) : null}
              {subtitle ? (
                <p className="text-sm text-muted-foreground">{subtitle}</p>
              ) : null}
            </div>

            <Card>
              <CardContent>{children}</CardContent>
            </Card>

            {appName && securedBy ? (
              <div className="mt-4 flex items-center justify-center gap-1.5 text-xs text-muted-foreground">
                <ShieldCheck className="size-3.5" />
                {securedBy}
              </div>
            ) : null}

            {footer ? (
              <div className="mt-4 text-center text-sm text-muted-foreground">
                {footer}
              </div>
            ) : null}
          </div>
        </main>

        {pageFooter ? (
          <footer className="text-center text-xs text-muted-foreground">
            {pageFooter}
          </footer>
        ) : null}
      </div>

      {/* Plain until a design has rendered in it, which is also when it starts to count for
          assistive technology: the design may hold links. */}
      <div
        aria-hidden={panelState === "rendered" ? undefined : true}
        data-slot="auth-panel"
        data-design-state={panelKey ? panelState : undefined}
        className="relative hidden overflow-hidden bg-muted lg:block"
      >
        {design && panelKey ? (
          <div className="absolute inset-0">
            <LoginDesignPanel
              key={panelKey}
              clientId={designClientId}
              design={design}
              onStateChange={(state) => setPanel({ key: panelKey, state })}
            />
          </div>
        ) : null}
      </div>
    </div>
  )
}

/**
 * A name in the corner, cut with an ellipsis when it does not fit.
 *
 * The ellipsis falls at the end of the cutting box in that box's own direction:
 * inheriting an RTL page's direction, a Latin name lost its beginning instead of
 * its end ("… Everyone Everywhere"). Its own direction keeps the start in view
 * in every language.
 */
function CornerName({ name }: { name: string }) {
  return (
    <bdi dir="auto" className="min-w-0 truncate text-sm font-semibold">
      {name}
    </bdi>
  )
}
