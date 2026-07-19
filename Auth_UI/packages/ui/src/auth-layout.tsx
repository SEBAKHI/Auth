import type * as React from "react"
import { ShieldCheck } from "lucide-react"

import { LanguageToggle } from "@astoom/ui/common/language-toggle"
import { ThemeToggle } from "@astoom/ui/common/theme-toggle"
import { Card, CardContent } from "@astoom/ui/card"
import { BrandingLogo } from "@astoom/ui/branding"

/** Centered card layout shared by all unauthenticated auth screens. */
export function AuthLayout({
  title,
  subtitle,
  children,
  footer,
  appName,
  appLogoUrl,
  securedBy,
}: {
  title: string
  subtitle?: string
  children: React.ReactNode
  footer?: React.ReactNode
  /**
   * Display name of the application behind a pending authorize request.
   * When set, the header shows that app's branding (hosted-login continuity)
   * and the layout renders the persistent `securedBy` trust marker.
   * Must come from the public-branding endpoint — never from URL parameters.
   */
  appName?: string | null
  /** Logo of that application; falls back to the platform mark when absent. */
  appLogoUrl?: string | null
  /** Trust marker under the card, e.g. "Secured by Astoom". */
  securedBy?: React.ReactNode
}) {
  const platformFallback = (
    <div className="mb-2 flex size-16 items-center justify-center rounded-2xl bg-primary text-primary-foreground">
      <ShieldCheck className="size-8" />
    </div>
  )

  return (
    <div className="relative flex min-h-svh items-center justify-center p-4">
      <div className="absolute end-4 top-4 flex items-center gap-1">
        <LanguageToggle />
        <ThemeToggle />
      </div>

      <div className="w-full max-w-sm">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          {appName ? (
            appLogoUrl ? (
              <img
                src={appLogoUrl}
                alt={appName}
                className="mb-2 h-20 w-auto max-w-64 object-contain"
              />
            ) : (
              platformFallback
            )
          ) : (
            <BrandingLogo
              className="mb-2 h-20 w-auto max-w-64 object-contain"
              fallback={platformFallback}
            />
          )}

          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
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
    </div>
  )
}
