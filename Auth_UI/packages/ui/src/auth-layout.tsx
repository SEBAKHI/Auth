import type * as React from "react"
import { ShieldCheck } from "lucide-react"

import { LanguageToggle } from "@authsystem/ui/common/language-toggle"
import { ThemeToggle } from "@authsystem/ui/common/theme-toggle"
import { Card, CardContent } from "@authsystem/ui/card"
import { BrandingLogo } from "@authsystem/ui/branding"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"
import { cn } from "@authsystem/ui/utils"

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
  showPlatformLogo = true,
  securedBy,
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
   */
  showPlatformLogo?: boolean
  /** Trust marker under the card, e.g. "Secured by Acme". */
  securedBy?: React.ReactNode
}) {
  const resolvedTheme = useResolvedTheme()
  const appLogo = appName
    ? pickLogo(appLogoUrl ?? null, appLogoUrlDark ?? null, resolvedTheme)
    : null
  const appTakesCorner = Boolean(appName) && !showPlatformLogo

  let corner: React.ReactNode
  if (appTakesCorner) {
    corner = appLogo ? (
      <img src={appLogo} alt={appName!} className="h-10 w-auto max-w-40 object-contain" />
    ) : (
      <span className="truncate text-sm font-semibold">{appName}</span>
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
            <span className="truncate text-sm font-semibold">{name}</span>
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
                  className="mb-2 h-12 w-auto max-w-60 object-contain"
                />
              ) : null}

              <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
              {appName && continueTo ? (
                <p className="text-sm text-muted-foreground">{continueTo}</p>
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

      <div aria-hidden data-slot="auth-panel" className="hidden bg-muted lg:block" />
    </div>
  )
}
