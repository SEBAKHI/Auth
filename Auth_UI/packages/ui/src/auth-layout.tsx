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
}: {
  title: string
  subtitle?: string
  children: React.ReactNode
  footer?: React.ReactNode
}) {
  return (
    <div className="relative flex min-h-svh items-center justify-center p-4">
      <div className="absolute end-4 top-4 flex items-center gap-1">
        <LanguageToggle />
        <ThemeToggle />
      </div>

      <div className="w-full max-w-sm">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          <BrandingLogo
            className="mb-2 h-20 w-auto max-w-64 object-contain"
            fallback={
              <div className="mb-2 flex size-16 items-center justify-center rounded-2xl bg-primary text-primary-foreground">
                <ShieldCheck className="size-8" />
              </div>
            }
          />

          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
          {subtitle ? (
            <p className="text-sm text-muted-foreground">{subtitle}</p>
          ) : null}
        </div>

        <Card>
          <CardContent>{children}</CardContent>
        </Card>

        {footer ? (
          <div className="mt-4 text-center text-sm text-muted-foreground">
            {footer}
          </div>
        ) : null}
      </div>
    </div>
  )
}
